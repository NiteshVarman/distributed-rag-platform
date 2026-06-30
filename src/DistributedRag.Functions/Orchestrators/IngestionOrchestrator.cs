using DistributedRag.Shared.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;

namespace DistributedRag.Functions.Orchestrators;

/// <summary>
/// Durable Functions orchestrator that coordinates the entire URL ingestion pipeline.
/// 
/// Flow:
///   1. ScraperActivity     → Fetch HTML, parse, extract clean text + structure
///   2. SmartChunkerActivity → Structure-aware chunking (paragraph, list, table, code)
///   3. Content hashing     → Skip if unchanged (deduplication)
///   4. EmbedderActivity    → Fan-out parallel embedding generation per chunk
///   5. FinalizeActivity    → Mark task as COMPLETED
/// 
/// Each activity updates task progress in MongoDB via the MongoDbService.
/// On failure, the task is marked as FAILED with the error message.
/// </summary>
public static class IngestionOrchestrator
{
    [Function("IngestionOrchestrator")]
    public static async Task RunOrchestrator(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var logger = context.CreateReplaySafeLogger("IngestionOrchestrator");
        var input = context.GetInput<ProcessUrlMessage>()!;
        var taskId = input.TaskId;
        var url = input.Url;

        logger.LogInformation("Orchestrator started — TaskId: {TaskId}, URL: {Url}", taskId, url);

        // Retry policy for activities: 3 attempts, 5s backoff, 2x coefficient, max 30s
        var retryPolicy = new TaskRetryOptions(new RetryPolicy(
            maxNumberOfAttempts: 3,
            firstRetryInterval: TimeSpan.FromSeconds(5),
            backoffCoefficient: 2.0,
            maxRetryInterval: TimeSpan.FromSeconds(30)));

        try
        {
            // ─── Step 1: Scrape the URL ───
            logger.LogInformation("[Step 1/5] Scraping URL: {Url}", url);

            var scraperInput = new ScraperInput { TaskId = taskId, Url = url };
            var scraperResult = await context.CallActivityAsync<ScraperResult>(
                "ScraperActivity", scraperInput, new TaskOptions(retryPolicy));

            if (!scraperResult.Success)
            {
                // Mark task as failed and exit
                await context.CallActivityAsync("FailTaskActivity",
                    new FailTaskInput { TaskId = taskId, ErrorMessage = scraperResult.Error ?? "Scraping failed" });
                return;
            }

            // ─── Step 2: Smart Chunking ───
            logger.LogInformation("[Step 2/5] Chunking content — {Length} chars", scraperResult.CleanedHtml?.Length ?? 0);

            var chunkerInput = new ChunkerInput
            {
                TaskId = taskId,
                CleanedHtml = scraperResult.CleanedHtml!,
                Title = scraperResult.Title ?? ""
            };
            var chunks = await context.CallActivityAsync<List<ChunkResult>>(
                "SmartChunkerActivity", chunkerInput, new TaskOptions(retryPolicy));

            if (chunks.Count == 0)
            {
                await context.CallActivityAsync("FailTaskActivity",
                    new FailTaskInput { TaskId = taskId, ErrorMessage = "No content chunks extracted from URL" });
                return;
            }

            logger.LogInformation("[Step 2/5] Produced {Count} chunks", chunks.Count);

            // ─── Step 3: Content Hashing (deduplication check) ───
            logger.LogInformation("[Step 3/5] Checking content hash for deduplication");

            var hashInput = new ContentHashInput
            {
                TaskId = taskId,
                Url = url,
                Title = scraperResult.Title ?? "",
                CleanedContent = scraperResult.CleanedHtml!
            };
            var hashResult = await context.CallActivityAsync<ContentHashResult>(
                "ContentHashActivity", hashInput);

            if (hashResult.IsUnchanged)
            {
                logger.LogInformation("Content unchanged — skipping embedding generation");
                await context.CallActivityAsync("FinalizeActivity",
                    new FinalizeInput { TaskId = taskId, Url = url, ContentHash = hashResult.ContentHash });
                return;
            }

            // ─── Step 4: Fan-out Parallel Embedding (batched) ───
            // One activity per BATCH of chunks (not per chunk): each batch makes a single
            // embedding call and a single bulk Mongo write, collapsing hundreds of sequential
            // round-trips into a handful — the dominant ingestion-speed win.
            const int embedBatchSize = 16;
            logger.LogInformation(
                "[Step 4/5] Embedding {Count} chunks in batches of {Batch} (fan-out)",
                chunks.Count, embedBatchSize);

            var embeddingTasks = new List<Task>();
            for (int start = 0; start < chunks.Count; start += embedBatchSize)
            {
                var batch = chunks.GetRange(start, Math.Min(embedBatchSize, chunks.Count - start));
                var batchInput = new EmbedderBatchInput
                {
                    TaskId = taskId,
                    Url = url,
                    ContentHash = hashResult.ContentHash,
                    Chunks = batch,
                    BatchStartIndex = start,
                    TotalChunks = chunks.Count
                };
                embeddingTasks.Add(context.CallActivityAsync(
                    "EmbedderActivity", batchInput, new TaskOptions(retryPolicy)));
            }

            // Wait for all embedding batches to complete
            await Task.WhenAll(embeddingTasks);

            logger.LogInformation("[Step 4/5] All {Count} embeddings generated", chunks.Count);

            // ─── Step 5: Finalize ───
            logger.LogInformation("[Step 5/5] Finalizing task");
            await context.CallActivityAsync("FinalizeActivity",
                new FinalizeInput { TaskId = taskId, Url = url, ContentHash = hashResult.ContentHash });

            logger.LogInformation("Orchestrator completed successfully — TaskId: {TaskId}", taskId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Orchestrator failed — TaskId: {TaskId}", taskId);

            // Mark the task as FAILED
            try
            {
                await context.CallActivityAsync("FailTaskActivity",
                    new FailTaskInput { TaskId = taskId, ErrorMessage = ex.Message });
            }
            catch (Exception failEx)
            {
                logger.LogError(failEx, "Failed to mark task as FAILED — TaskId: {TaskId}", taskId);
            }
        }
    }
}

// ─────────────────────────────────────────────
// Activity Input/Output DTOs
// ─────────────────────────────────────────────

public class ScraperInput
{
    public string TaskId { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}

public class ScraperResult
{
    public bool Success { get; set; }
    public string? Title { get; set; }
    public string? CleanedHtml { get; set; }
    public string? Error { get; set; }
}

public class ChunkerInput
{
    public string TaskId { get; set; } = string.Empty;
    public string CleanedHtml { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
}

public class ContentHashInput
{
    public string TaskId { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string CleanedContent { get; set; } = string.Empty;
}

public class ContentHashResult
{
    public string ContentHash { get; set; } = string.Empty;
    public bool IsUnchanged { get; set; }
}

public class EmbedderBatchInput
{
    public string TaskId { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>The chunks in this batch (a contiguous slice of the document).</summary>
    public List<ChunkResult> Chunks { get; set; } = new();

    /// <summary>Global position of the first chunk in this batch — used to derive
    /// each chunk's stable ChunkIndex (BatchStartIndex + offset).</summary>
    public int BatchStartIndex { get; set; }

    public int TotalChunks { get; set; }
}

public class FailTaskInput
{
    public string TaskId { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
}

public class FinalizeInput
{
    public string TaskId { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
}
