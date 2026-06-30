using DistributedRag.Functions.Orchestrators;
using DistributedRag.Shared.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace DistributedRag.Functions.Activities;

/// <summary>
/// Activity: Performs blue/green cleanup of stale embeddings, then marks the
/// task as COMPLETED with progress = 100%.
///
/// This is the single finalization point — only this activity sets
/// the task to COMPLETED status. This ensures atomic state transitions
/// and prevents race conditions from parallel embedding activities.
///
/// Cleanup runs AFTER the new embeddings are written: any embeddings for the
/// same URL with a different content hash are deleted here, so re-processing
/// never leaves the URL without searchable content.
/// </summary>
public class FinalizeActivity
{
    private readonly MongoDbService _mongoDb;
    private readonly ILogger<FinalizeActivity> _logger;

    public FinalizeActivity(MongoDbService mongoDb, ILogger<FinalizeActivity> logger)
    {
        _mongoDb = mongoDb;
        _logger = logger;
    }

    [Function("FinalizeActivity")]
    public async Task Run(
        [ActivityTrigger] FinalizeInput input)
    {
        _logger.LogInformation("Finalizing task: {TaskId}", input.TaskId);

        // Remove any embeddings for this URL left over from a previous content
        // version. Safe no-op when the hash is unchanged (nothing to delete).
        if (!string.IsNullOrEmpty(input.Url) && !string.IsNullOrEmpty(input.ContentHash))
        {
            await _mongoDb.DeleteStaleEmbeddingsAsync(input.Url, input.ContentHash);
        }

        await _mongoDb.CompleteTaskAsync(input.TaskId);
        _logger.LogInformation("Task completed: {TaskId}", input.TaskId);
    }
}
