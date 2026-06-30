using DistributedRag.Shared.Models;
using Microsoft.Extensions.Logging;

namespace DistributedRag.Shared.Services;

/// <summary>
/// The core RAG query pipeline — ties together all services:
///   1. Embed the user's question → Gemini text-embedding-004 (768-dim vector)
///   2. Vector search → MongoDB Atlas $vectorSearch (semantic similarity)
///   3. Text search → MongoDB Atlas $search (keyword matching)
///   4. Merge + deduplicate + rank results (Reciprocal Rank Fusion)
///   5. Build context prompt → Groq LLM (llama-3.3-70b)
///   6. Return answer + source chunks
/// 
/// This is the brain of the RAG platform — everything converges here.
/// </summary>
public class RagQueryService
{
    private readonly MongoDbService _mongoDb;
    private readonly IEmbeddingService _embeddingService;
    private readonly GroqLlmService _llmService;
    private readonly ILogger<RagQueryService> _logger;

    // Search parameters — tuned for quality
    private const int VectorSearchLimit = 5;
    private const int VectorSearchCandidates = 100;
    private const int TextSearchLimit = 5;
    private const int MaxContextChunks = 8;       // Cap total chunks sent to LLM
    private const int MaxContextChars = 12000;     // Prevent exceeding token limits

    public RagQueryService(
        MongoDbService mongoDb,
        IEmbeddingService embeddingService,
        GroqLlmService llmService,
        ILogger<RagQueryService> logger)
    {
        _mongoDb = mongoDb;
        _embeddingService = embeddingService;
        _llmService = llmService;
        _logger = logger;
    }

    /// <summary>
    /// Executes the full RAG pipeline: embed → search → merge → prompt → answer.
    /// </summary>
    /// <param name="url">The URL to scope the search to.</param>
    /// <param name="question">The user's natural language question.</param>
    /// <returns>A QueryResponse with the answer and source chunks.</returns>
    public async Task<QueryResponse> QueryAsync(string url, string question)
    {
        _logger.LogInformation("RAG query started — URL: {Url}, Question: {Question}", url, question);

        // ─── Step 1: Embed the question ───
        _logger.LogInformation("[Step 1/5] Embedding question...");

        float[] queryEmbedding;
        try
        {
            queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(question);
            _logger.LogInformation("Question embedded — {Dims} dimensions", queryEmbedding.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to embed question");
            return new QueryResponse
            {
                Answer = "Sorry, I couldn't process your question right now. " +
                         "The embedding service is unavailable. Please try again later.",
                Sources = []
            };
        }

        // ─── Step 2: Vector search (semantic) ───
        _logger.LogInformation("[Step 2/5] Running vector search...");

        List<EmbeddingSearchResult> vectorResults;
        try
        {
            vectorResults = await _mongoDb.VectorSearchAsync(
                queryEmbedding,
                url: url,
                limit: VectorSearchLimit,
                numCandidates: VectorSearchCandidates);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Vector search failed — falling back to text search only");
            vectorResults = [];
        }

        _logger.LogInformation("Vector search returned {Count} results", vectorResults.Count);

        // ─── Step 3: Text search (keyword) ───
        _logger.LogInformation("[Step 3/5] Running text search...");

        List<EmbeddingSearchResult> textResults;
        try
        {
            textResults = await _mongoDb.TextSearchAsync(
                question,
                url: url,
                limit: TextSearchLimit);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Text search failed — using vector results only");
            textResults = [];
        }

        _logger.LogInformation("Text search returned {Count} results", textResults.Count);

        // ─── Step 4: Merge + deduplicate + rank ───
        _logger.LogInformation("[Step 4/5] Merging and ranking results...");

        var mergedResults = MergeAndRank(vectorResults, textResults);

        if (mergedResults.Count == 0)
        {
            _logger.LogWarning("No results found for URL: {Url}", url);
            return new QueryResponse
            {
                Answer = "I couldn't find any processed content for this URL. " +
                         "Please make sure the URL has been submitted for processing " +
                         "and the task has completed successfully.",
                Sources = []
            };
        }

        _logger.LogInformation("Merged into {Count} unique chunks", mergedResults.Count);

        // ─── Step 5: Generate LLM answer ───
        _logger.LogInformation("[Step 5/5] Generating answer with LLM...");

        var contextChunks = mergedResults
            .Select(r => r.Chunk)
            .ToList();

        string answer;
        try
        {
            answer = await _llmService.GenerateAnswerAsync(contextChunks, question);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM failed to generate answer");
            answer = "Sorry, I couldn't generate an answer right now. " +
                     "The LLM service is unavailable. Please try again later.";
        }

        // ─── Build response ───
        var sources = mergedResults.Select(r => new SourceChunk
        {
            Chunk = r.Chunk,
            ChunkType = r.ChunkType,
            Score = Math.Round(r.Score, 4)
        }).ToList();

        _logger.LogInformation(
            "RAG query completed — Answer: {Length} chars, Sources: {Count}",
            answer.Length, sources.Count);

        return new QueryResponse
        {
            Answer = answer,
            Sources = sources
        };
    }

    /// <summary>
    /// Merges vector search and text search results using Reciprocal Rank Fusion (RRF).
    /// 
    /// RRF formula: score = Σ 1 / (k + rank_i) for each ranking list
    /// where k = 60 (standard constant that balances contribution of top vs. lower ranks).
    /// 
    /// This is the same algorithm used by Elasticsearch, Pinecone, and MongoDB Atlas
    /// for hybrid search. It effectively combines semantic (vector) and keyword (text)
    /// relevance without needing to normalize scores from different systems.
    /// </summary>
    private List<EmbeddingSearchResult> MergeAndRank(
        List<EmbeddingSearchResult> vectorResults,
        List<EmbeddingSearchResult> textResults)
    {
        const double k = 60.0; // RRF constant

        // Score each result by RRF
        var rrfScores = new Dictionary<string, (EmbeddingSearchResult Result, double RrfScore)>();

        // Score vector results
        for (int i = 0; i < vectorResults.Count; i++)
        {
            var result = vectorResults[i];
            var rrfScore = 1.0 / (k + i + 1); // rank is 1-indexed

            if (rrfScores.TryGetValue(result.Chunk, out var existing))
            {
                rrfScores[result.Chunk] = (existing.Result, existing.RrfScore + rrfScore);
            }
            else
            {
                rrfScores[result.Chunk] = (result, rrfScore);
            }
        }

        // Score text results
        for (int i = 0; i < textResults.Count; i++)
        {
            var result = textResults[i];
            var rrfScore = 1.0 / (k + i + 1);

            if (rrfScores.TryGetValue(result.Chunk, out var existing))
            {
                // Chunk appears in both vector and text results — boost it
                rrfScores[result.Chunk] = (existing.Result, existing.RrfScore + rrfScore);
            }
            else
            {
                rrfScores[result.Chunk] = (result, rrfScore);
            }
        }

        // Sort by RRF score descending, take top N, enforce character limit
        var ranked = rrfScores.Values
            .OrderByDescending(x => x.RrfScore)
            .Take(MaxContextChunks)
            .ToList();

        // Enforce total context character limit to prevent token overflow
        var finalResults = new List<EmbeddingSearchResult>();
        var totalChars = 0;

        foreach (var (result, rrfScore) in ranked)
        {
            if (totalChars + result.Chunk.Length > MaxContextChars)
                break;

            // Store the RRF score as the final score
            result.Score = rrfScore;
            finalResults.Add(result);
            totalChars += result.Chunk.Length;
        }

        return finalResults;
    }
}
