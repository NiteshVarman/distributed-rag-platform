using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using DistributedRag.Shared.Configuration;
using DistributedRag.Shared.Models;

namespace DistributedRag.Shared.Services;

/// <summary>
/// Centralized MongoDB service for all database operations.
/// Handles CRUD for BackgroundTasks and Embeddings,
/// plus vector search and text search for the RAG query pipeline.
/// 
/// Registered as a singleton — MongoClient is thread-safe and connection-pooled.
/// </summary>
public class MongoDbService
{
    private readonly IMongoDatabase _database;
    private readonly IMongoCollection<BackgroundTask> _tasksCollection;
    private readonly IMongoCollection<EmbeddingDocument> _embeddingsCollection;
    private readonly ILogger<MongoDbService> _logger;

    // Index names — must match what's configured in MongoDB Atlas
    private const string VectorIndexName = "vector_index";
    private const string TextIndexName = "text_index";

    public MongoDbService(IOptions<MongoDbSettings> settings, ILogger<MongoDbService> logger)
    {
        _logger = logger;

        var mongoSettings = settings.Value;

        if (string.IsNullOrWhiteSpace(mongoSettings.ConnectionString))
            throw new InvalidOperationException("MongoDB connection string is not configured. Set 'MongoDB:ConnectionString' in appsettings.json or environment variables.");

        var client = new MongoClient(mongoSettings.ConnectionString);
        _database = client.GetDatabase(mongoSettings.DatabaseName);
        _tasksCollection = _database.GetCollection<BackgroundTask>("backgroundTasks");
        _embeddingsCollection = _database.GetCollection<EmbeddingDocument>("embeddings");

        _logger.LogInformation("MongoDbService initialized — Database: {Database}", mongoSettings.DatabaseName);
    }

    // ─────────────────────────────────────────────
    // BACKGROUND TASKS — CRUD
    // ─────────────────────────────────────────────

    /// <summary>
    /// Creates a new background task in QUEUED status.
    /// </summary>
    public async Task<BackgroundTask> CreateTaskAsync(string taskId, string url)
    {
        var task = new BackgroundTask
        {
            Id = taskId,
            Url = url,
            Status = TaskProcessingStatus.QUEUED,
            Progress = 0,
            CurrentStep = "Queued for processing",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _tasksCollection.InsertOneAsync(task);
        _logger.LogInformation("Task created: {TaskId} for URL: {Url}", taskId, url);
        return task;
    }

    /// <summary>
    /// Gets a task by its ID.
    /// </summary>
    public async Task<BackgroundTask?> GetTaskAsync(string taskId)
    {
        var filter = Builders<BackgroundTask>.Filter.Eq(t => t.Id, taskId);
        return await _tasksCollection.Find(filter).FirstOrDefaultAsync();
    }

    /// <summary>
    /// Updates the task's progress, current step, and status.
    /// Called by orchestrator activities to report progress.
    /// </summary>
    public async Task UpdateTaskProgressAsync(string taskId, TaskProcessingStatus status, int progress, string currentStep)
    {
        var filter = Builders<BackgroundTask>.Filter.Eq(t => t.Id, taskId);
        var update = Builders<BackgroundTask>.Update
            .Set(t => t.Status, status)
            .Set(t => t.Progress, progress)
            .Set(t => t.CurrentStep, currentStep)
            .Set(t => t.UpdatedAt, DateTime.UtcNow);

        await _tasksCollection.UpdateOneAsync(filter, update);
        _logger.LogInformation("Task {TaskId} updated: {Status} ({Progress}%) — {Step}", taskId, status, progress, currentStep);
    }

    /// <summary>
    /// Sets the content hash for a task (used for deduplication on re-processing).
    /// </summary>
    public async Task SetTaskContentHashAsync(string taskId, string contentHash)
    {
        var filter = Builders<BackgroundTask>.Filter.Eq(t => t.Id, taskId);
        var update = Builders<BackgroundTask>.Update
            .Set(t => t.ContentHash, contentHash)
            .Set(t => t.UpdatedAt, DateTime.UtcNow);

        await _tasksCollection.UpdateOneAsync(filter, update);
    }

    /// <summary>
    /// Marks a task as FAILED with an error message.
    /// </summary>
    public async Task FailTaskAsync(string taskId, string errorMessage)
    {
        var filter = Builders<BackgroundTask>.Filter.Eq(t => t.Id, taskId);
        var update = Builders<BackgroundTask>.Update
            .Set(t => t.Status, TaskProcessingStatus.FAILED)
            .Set(t => t.Error, errorMessage)
            .Set(t => t.CurrentStep, "Failed")
            .Set(t => t.UpdatedAt, DateTime.UtcNow);

        await _tasksCollection.UpdateOneAsync(filter, update);
        _logger.LogError("Task {TaskId} failed: {Error}", taskId, errorMessage);
    }

    /// <summary>
    /// Marks a task as COMPLETED (progress = 100).
    /// This is the single finalization point — called only by FinalizeActivity.
    /// </summary>
    public async Task CompleteTaskAsync(string taskId)
    {
        var filter = Builders<BackgroundTask>.Filter.Eq(t => t.Id, taskId);
        var update = Builders<BackgroundTask>.Update
            .Set(t => t.Status, TaskProcessingStatus.COMPLETED)
            .Set(t => t.Progress, 100)
            .Set(t => t.CurrentStep, "Completed")
            .Set(t => t.UpdatedAt, DateTime.UtcNow);

        await _tasksCollection.UpdateOneAsync(filter, update);
        _logger.LogInformation("Task {TaskId} completed successfully", taskId);
    }

    /// <summary>
    /// Gets the most recent task for a URL regardless of status (for page-status checks).
    /// </summary>
    public async Task<BackgroundTask?> GetLatestTaskByUrlAsync(string url)
    {
        var filter = Builders<BackgroundTask>.Filter.Eq(t => t.Url, url);
        return await _tasksCollection
            .Find(filter)
            .SortByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Checks if a URL has already been processed with the same content hash.
    /// If so, we can skip re-processing (deduplication).
    /// </summary>
    public async Task<string?> GetExistingContentHashForUrlAsync(string url)
    {
        var filter = Builders<BackgroundTask>.Filter.And(
            Builders<BackgroundTask>.Filter.Eq(t => t.Url, url),
            Builders<BackgroundTask>.Filter.Eq(t => t.Status, TaskProcessingStatus.COMPLETED)
        );

        var existingTask = await _tasksCollection
            .Find(filter)
            .SortByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync();

        return existingTask?.ContentHash;
    }

    // ─────────────────────────────────────────────
    // EMBEDDINGS — CRUD
    // ─────────────────────────────────────────────

    /// <summary>
    /// Idempotently stores a BATCH of embeddings in a single round-trip.
    /// Uses one unordered BulkWrite of upserts keyed on (url, contentHash, chunkIndex),
    /// replacing per-chunk inserts. This is the main ingestion-throughput optimization.
    /// </summary>
    public async Task UpsertEmbeddingsAsync(IReadOnlyList<EmbeddingDocument> embeddings)
    {
        if (embeddings.Count == 0) return;

        var models = new List<WriteModel<EmbeddingDocument>>(embeddings.Count);
        foreach (var e in embeddings)
        {
            var filter = Builders<EmbeddingDocument>.Filter.And(
                Builders<EmbeddingDocument>.Filter.Eq(x => x.Url, e.Url),
                Builders<EmbeddingDocument>.Filter.Eq(x => x.ContentHash, e.ContentHash),
                Builders<EmbeddingDocument>.Filter.Eq(x => x.ChunkIndex, e.ChunkIndex));

            // $set only (never _id) so the server generates _id on insert.
            var update = Builders<EmbeddingDocument>.Update
                .Set(x => x.TaskId, e.TaskId)
                .Set(x => x.Chunk, e.Chunk)
                .Set(x => x.ChunkType, e.ChunkType)
                .Set(x => x.Embedding, e.Embedding)
                .Set(x => x.CreatedAt, e.CreatedAt);

            models.Add(new UpdateOneModel<EmbeddingDocument>(filter, update) { IsUpsert = true });
        }

        await _embeddingsCollection.BulkWriteAsync(models, new BulkWriteOptions { IsOrdered = false });
        _logger.LogInformation("Bulk-upserted {Count} embeddings", embeddings.Count);
    }

    /// <summary>
    /// Advances embedding progress, moving it FORWARD only. Parallel embedding
    /// batches finish out of order, so a plain Set could make the bar jump backward;
    /// the Lt(progress) guard keeps it monotonic.
    /// </summary>
    public async Task AdvanceEmbeddingProgressAsync(string taskId, int progress, string currentStep)
    {
        var filter = Builders<BackgroundTask>.Filter.And(
            Builders<BackgroundTask>.Filter.Eq(t => t.Id, taskId),
            Builders<BackgroundTask>.Filter.Lt(t => t.Progress, progress));
        var update = Builders<BackgroundTask>.Update
            .Set(t => t.Status, TaskProcessingStatus.PROCESSING)
            .Set(t => t.Progress, progress)
            .Set(t => t.CurrentStep, currentStep)
            .Set(t => t.UpdatedAt, DateTime.UtcNow);

        await _tasksCollection.UpdateOneAsync(filter, update);
    }

    /// <summary>
    /// Deletes all embeddings for a given URL.
    /// Called before re-processing when content has changed.
    /// </summary>
    public async Task<long> DeleteEmbeddingsByUrlAsync(string url)
    {
        var filter = Builders<EmbeddingDocument>.Filter.Eq(e => e.Url, url);
        var result = await _embeddingsCollection.DeleteManyAsync(filter);
        _logger.LogInformation("Deleted {Count} old embeddings for URL: {Url}", result.DeletedCount, url);
        return result.DeletedCount;
    }

    /// <summary>
    /// Blue/green cleanup: deletes embeddings for a URL whose content hash does NOT
    /// match the current one. Called at finalization, after the new embeddings have
    /// already been written, so queries never see a gap where the URL has no content.
    /// </summary>
    public async Task<long> DeleteStaleEmbeddingsAsync(string url, string currentContentHash)
    {
        var filter = Builders<EmbeddingDocument>.Filter.And(
            Builders<EmbeddingDocument>.Filter.Eq(e => e.Url, url),
            Builders<EmbeddingDocument>.Filter.Ne(e => e.ContentHash, currentContentHash));

        var result = await _embeddingsCollection.DeleteManyAsync(filter);
        if (result.DeletedCount > 0)
            _logger.LogInformation("Deleted {Count} stale embeddings for URL: {Url}", result.DeletedCount, url);
        return result.DeletedCount;
    }

    /// <summary>
    /// Deletes all embeddings for a given task ID.
    /// Used for cleanup on failure or re-processing.
    /// </summary>
    public async Task<long> DeleteEmbeddingsByTaskIdAsync(string taskId)
    {
        var filter = Builders<EmbeddingDocument>.Filter.Eq(e => e.TaskId, taskId);
        var result = await _embeddingsCollection.DeleteManyAsync(filter);
        return result.DeletedCount;
    }

    // ─────────────────────────────────────────────
    // RAG QUERY — VECTOR SEARCH
    // ─────────────────────────────────────────────

    /// <summary>
    /// Performs MongoDB Atlas Vector Search ($vectorSearch) to find the most
    /// semantically similar chunks to the query embedding.
    /// 
    /// Requires the 'vector_index' Atlas Search index on the embeddings collection.
    /// </summary>
    /// <param name="queryEmbedding">The query vector (384 dimensions).</param>
    /// <param name="url">Optional URL filter to scope search to a specific page.</param>
    /// <param name="limit">Maximum number of results to return.</param>
    /// <param name="numCandidates">Number of candidates to consider (higher = more accurate but slower).</param>
    /// <returns>List of matching embedding documents with search scores.</returns>
    public async Task<List<EmbeddingSearchResult>> VectorSearchAsync(
        float[] queryEmbedding,
        string? url = null,
        int limit = 5,
        int numCandidates = 100)
    {
        var vectorSearchStage = new BsonDocument("$vectorSearch", new BsonDocument
        {
            { "index", VectorIndexName },
            { "path", "embedding" },
            { "queryVector", new BsonArray(queryEmbedding.Select(f => (double)f)) },
            { "numCandidates", numCandidates },
            { "limit", limit }
        });

        // Add URL filter if specified
        if (!string.IsNullOrWhiteSpace(url))
        {
            vectorSearchStage["$vectorSearch"]["filter"] = new BsonDocument("url", url);
        }

        var projectStage = new BsonDocument("$project", new BsonDocument
        {
            { "_id", 1 },
            { "taskId", 1 },
            { "url", 1 },
            { "chunk", 1 },
            { "chunkType", 1 },
            { "score", new BsonDocument("$meta", "vectorSearchScore") }
        });

        var pipeline = PipelineDefinition<EmbeddingDocument, BsonDocument>.Create(
            vectorSearchStage, projectStage);

        var results = await _embeddingsCollection.Aggregate(pipeline).ToListAsync();

        _logger.LogInformation("Vector search returned {Count} results", results.Count);

        return results.Select(doc => new EmbeddingSearchResult
        {
            Id = doc["_id"].ToString()!,
            TaskId = doc["taskId"].AsString,
            Url = doc["url"].AsString,
            Chunk = doc["chunk"].AsString,
            ChunkType = doc.Contains("chunkType") ? doc["chunkType"].AsString : "paragraph",
            Score = doc["score"].AsDouble
        }).ToList();
    }

    // ─────────────────────────────────────────────
    // RAG QUERY — TEXT SEARCH (KEYWORD)
    // ─────────────────────────────────────────────

    /// <summary>
    /// Performs MongoDB Atlas Text Search ($search) to find keyword-matching chunks.
    /// Complements vector search for hybrid retrieval.
    /// 
    /// Requires the 'text_index' Atlas Search index on the embeddings collection.
    /// </summary>
    /// <param name="query">The text query to search for.</param>
    /// <param name="url">Optional URL filter to scope search to a specific page.</param>
    /// <param name="limit">Maximum number of results to return.</param>
    /// <returns>List of matching embedding documents with search scores.</returns>
    public async Task<List<EmbeddingSearchResult>> TextSearchAsync(
        string query,
        string? url = null,
        int limit = 5)
    {
        var searchStage = new BsonDocument("$search", new BsonDocument
        {
            { "index", TextIndexName },
            { "text", new BsonDocument
                {
                    { "query", query },
                    { "path", "chunk" }
                }
            }
        });

        // Build the pipeline stages
        var stages = new List<BsonDocument> { searchStage };

        // Add URL filter if specified
        if (!string.IsNullOrWhiteSpace(url))
        {
            stages.Add(new BsonDocument("$match", new BsonDocument("url", url)));
        }

        // Limit results
        stages.Add(new BsonDocument("$limit", limit));

        // Project with score
        stages.Add(new BsonDocument("$project", new BsonDocument
        {
            { "_id", 1 },
            { "taskId", 1 },
            { "url", 1 },
            { "chunk", 1 },
            { "chunkType", 1 },
            { "score", new BsonDocument("$meta", "searchScore") }
        }));

        var pipeline = PipelineDefinition<EmbeddingDocument, BsonDocument>.Create(stages);
        var results = await _embeddingsCollection.Aggregate(pipeline).ToListAsync();

        _logger.LogInformation("Text search returned {Count} results for query: {Query}", results.Count, query);

        return results.Select(doc => new EmbeddingSearchResult
        {
            Id = doc["_id"].ToString()!,
            TaskId = doc["taskId"].AsString,
            Url = doc["url"].AsString,
            Chunk = doc["chunk"].AsString,
            ChunkType = doc.Contains("chunkType") ? doc["chunkType"].AsString : "paragraph",
            Score = doc["score"].AsDouble
        }).ToList();
    }

    // ─────────────────────────────────────────────
    // HEALTH CHECK
    // ─────────────────────────────────────────────

    /// <summary>
    /// Verifies connectivity to MongoDB Atlas.
    /// Used for startup health checks and diagnostics.
    /// </summary>
    public async Task<bool> HealthCheckAsync()
    {
        try
        {
            var result = await _database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1));
            _logger.LogInformation("MongoDB health check passed");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MongoDB health check failed");
            return false;
        }
    }
}

/// <summary>
/// DTO for search results (both vector and text search).
/// Includes the search score for ranking/merging.
/// </summary>
public class EmbeddingSearchResult
{
    public string Id { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Chunk { get; set; } = string.Empty;
    public string ChunkType { get; set; } = "paragraph";

    /// <summary>
    /// Search relevance score (vector cosine similarity or text search score).
    /// </summary>
    public double Score { get; set; }
}
