using DistributedRag.Functions.Orchestrators;
using DistributedRag.Shared.Models;
using DistributedRag.Shared.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace DistributedRag.Functions.Activities;

/// <summary>
/// Activity: embeds a BATCH of chunks and stores them in one bulk write.
///
/// The orchestrator fans out one instance per batch (not per chunk). Each instance:
///   1. Makes a SINGLE batched embedding call for all chunks in the batch.
///   2. Bulk-upserts the resulting embeddings in one round-trip (idempotent on
///      url + contentHash + chunkIndex, so a Durable retry is safe).
///   3. Advances progress once (forward-only).
///
/// This collapses the previous per-chunk pattern (N embedding calls + 2N Mongo writes)
/// into ~N/batchSize operations — the main ingestion-throughput optimization.
/// </summary>
public class EmbedderActivity
{
    private readonly MongoDbService _mongoDb;
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<EmbedderActivity> _logger;

    public EmbedderActivity(
        MongoDbService mongoDb,
        IEmbeddingService embeddingService,
        ILogger<EmbedderActivity> logger)
    {
        _mongoDb = mongoDb;
        _embeddingService = embeddingService;
        _logger = logger;
    }

    [Function("EmbedderActivity")]
    public async Task Run([ActivityTrigger] EmbedderBatchInput input)
    {
        var count = input.Chunks.Count;
        if (count == 0) return;

        _logger.LogInformation(
            "Embedding batch of {Count} chunks (start {Start}/{Total}) for task {TaskId}",
            count, input.BatchStartIndex, input.TotalChunks, input.TaskId);

        // ─── Single batched embedding call ───
        var texts = input.Chunks.Select(c => c.Content).ToList();
        var embeddings = await _embeddingService.GenerateEmbeddingsAsync(texts);

        if (embeddings.Count != count)
            throw new InvalidOperationException(
                $"Embedding count mismatch: got {embeddings.Count}, expected {count}");

        // ─── Build documents with stable, contiguous global chunk indices ───
        var docs = new List<EmbeddingDocument>(count);
        for (int j = 0; j < count; j++)
        {
            docs.Add(new EmbeddingDocument
            {
                TaskId = input.TaskId,
                Url = input.Url,
                Chunk = input.Chunks[j].Content,
                ChunkType = input.Chunks[j].ChunkType,
                ChunkIndex = input.BatchStartIndex + j,
                Embedding = embeddings[j],
                ContentHash = input.ContentHash,
                CreatedAt = DateTime.UtcNow
            });
        }

        // ─── One bulk upsert for the whole batch ───
        await _mongoDb.UpsertEmbeddingsAsync(docs);

        // ─── One forward-only progress update (50% → 90% across embedding) ───
        var done = input.BatchStartIndex + count;
        var progress = Math.Min(90, (int)(50 + 40.0 * done / Math.Max(1, input.TotalChunks)));
        await _mongoDb.AdvanceEmbeddingProgressAsync(
            input.TaskId, progress, $"Embedded {done}/{input.TotalChunks} chunks");

        _logger.LogInformation(
            "Stored batch of {Count} embeddings ({Done}/{Total}) — Task: {TaskId}",
            count, done, input.TotalChunks, input.TaskId);
    }
}
