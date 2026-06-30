using DistributedRag.Functions.Orchestrators;
using DistributedRag.Shared.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace DistributedRag.Functions.Activities;

/// <summary>
/// Activity: Marks a task as FAILED with an error message.
/// 
/// Called by the orchestrator when any step in the pipeline fails.
/// This centralizes error handling — the orchestrator catches exceptions
/// and delegates failure recording to this activity.
/// </summary>
public class FailTaskActivity
{
    private readonly MongoDbService _mongoDb;
    private readonly ILogger<FailTaskActivity> _logger;

    public FailTaskActivity(MongoDbService mongoDb, ILogger<FailTaskActivity> logger)
    {
        _mongoDb = mongoDb;
        _logger = logger;
    }

    [Function("FailTaskActivity")]
    public async Task Run(
        [ActivityTrigger] FailTaskInput input)
    {
        _logger.LogError("Marking task as FAILED — TaskId: {TaskId}, Error: {Error}",
            input.TaskId, input.ErrorMessage);
        await _mongoDb.FailTaskAsync(input.TaskId, input.ErrorMessage);
    }
}
