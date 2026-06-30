using DistributedRag.Shared.Models;
using DistributedRag.Shared.Services;

namespace DistributedRag.Api.Endpoints;

/// <summary>
/// GET /api/job-status/{taskId}
/// 
/// Returns the current status and progress of a background processing task.
/// The Chrome Extension polls this endpoint to track ingestion progress.
/// </summary>
public static class JobStatusEndpoint
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/job-status/{taskId}", HandleAsync)
            .WithName("JobStatus")
            .WithTags("Ingestion")
            .Produces<JobStatusResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithDescription("Get the current status and progress of a processing task.");
    }

    private static async Task<IResult> HandleAsync(
        string taskId,
        MongoDbService mongoDb,
        ILogger<JobStatusResponse> logger)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return Results.BadRequest(new { error = "Task ID is required" });
        }

        var task = await mongoDb.GetTaskAsync(taskId);

        if (task is null)
        {
            logger.LogWarning("Task not found: {TaskId}", taskId);
            return Results.NotFound(new { error = $"Task '{taskId}' not found" });
        }

        var response = new JobStatusResponse
        {
            TaskId = task.Id,
            Url = task.Url,
            Status = task.Status.ToString(),
            Progress = task.Progress,
            CurrentStep = task.CurrentStep,
            Error = task.Error,
            CreatedAt = task.CreatedAt,
            UpdatedAt = task.UpdatedAt
        };

        return Results.Ok(response);
    }
}
