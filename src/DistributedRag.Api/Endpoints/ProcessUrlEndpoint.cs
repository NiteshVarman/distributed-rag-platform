using DistributedRag.Api.Services;
using DistributedRag.Shared.Models;
using DistributedRag.Shared.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DistributedRag.Api.Endpoints;

/// <summary>
/// POST /api/process-url
/// 
/// Accepts a URL, creates a background task in MongoDB,
/// publishes a message to Azure Service Bus, and returns the task ID.
/// The caller can then poll /api/job-status/{taskId} for progress.
/// </summary>
public static class ProcessUrlEndpoint
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/api/process-url", HandleAsync)
            .WithName("ProcessUrl")
            .WithTags("Ingestion")
            .Accepts<ProcessUrlRequest>("application/json")
            .Produces<ProcessUrlResponse>(StatusCodes.Status202Accepted)
            .Produces<ProblemHttpResult>(StatusCodes.Status400BadRequest)
            .WithDescription("Submit a URL for RAG ingestion. Returns a taskId for status polling.");
    }

    private static async Task<IResult> HandleAsync(
        ProcessUrlRequest request,
        MongoDbService mongoDb,
        ServiceBusPublisher serviceBus,
        ILogger<ProcessUrlRequest> logger)
    {
        // ─── Validate URL ───
        if (string.IsNullOrWhiteSpace(request.Url))
        {
            return Results.BadRequest(new { error = "URL is required" });
        }

        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            return Results.BadRequest(new { error = "A valid HTTP/HTTPS URL is required" });
        }

        // Normalize the URL (remove trailing slash, fragments)
        var normalizedUrl = uri.GetLeftPart(UriPartial.Query).TrimEnd('/');

        // ─── Generate Task ID ───
        var taskId = Guid.NewGuid().ToString("N");

        // ─── Create task in MongoDB (QUEUED status) ───
        try
        {
            await mongoDb.CreateTaskAsync(taskId, normalizedUrl);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create task in MongoDB");
            return Results.Problem(
                detail: "Failed to create processing task. Please try again.",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        // ─── Publish message to Service Bus ───
        try
        {
            var message = new ProcessUrlMessage
            {
                TaskId = taskId,
                Url = normalizedUrl
            };
            await serviceBus.PublishProcessUrlMessageAsync(message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to publish to Service Bus. Marking task as FAILED.");
            await mongoDb.FailTaskAsync(taskId, "Failed to queue processing: " + ex.Message);
            return Results.Problem(
                detail: "Failed to queue URL for processing. Please try again.",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        // ─── Return 202 Accepted ───
        var response = new ProcessUrlResponse
        {
            TaskId = taskId,
            Status = "QUEUED",
            Message = $"URL queued for processing. Poll /api/job-status/{taskId} for progress."
        };

        logger.LogInformation("URL submitted for processing — TaskId: {TaskId}, URL: {Url}", taskId, normalizedUrl);

        return Results.Accepted($"/api/job-status/{taskId}", response);
    }
}
