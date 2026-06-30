using DistributedRag.Shared.Models;
using DistributedRag.Shared.Services;

namespace DistributedRag.Api.Endpoints;

/// <summary>
/// GET /api/page-status?url=...
///
/// Tells the client whether a page has already been ingested, plus the latest
/// task's progress. Used by the Ask AI panel to decide whether to auto-process
/// the page (and to show indexing progress) instead of requiring a manual step.
/// </summary>
public static class PageStatusEndpoint
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/page-status", HandleAsync)
            .WithName("PageStatus")
            .WithTags("Ingestion")
            .WithDescription("Returns whether a URL is processed and the latest task's progress.");
    }

    private static async Task<IResult> HandleAsync(string? url, MongoDbService mongoDb)
    {
        if (string.IsNullOrWhiteSpace(url))
            return Results.BadRequest(new { error = "url is required" });

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
            return Results.BadRequest(new { error = "A valid HTTP/HTTPS url is required" });

        // Normalize identically to ProcessUrlEndpoint / QueryEndpoint.
        var normalized = uri.GetLeftPart(UriPartial.Query).TrimEnd('/');
        var task = await mongoDb.GetLatestTaskByUrlAsync(normalized);

        return Results.Ok(new
        {
            url = normalized,
            exists = task != null,
            processed = task?.Status == TaskProcessingStatus.COMPLETED,
            status = task?.Status.ToString(),
            progress = task?.Progress ?? 0,
            currentStep = task?.CurrentStep,
            taskId = task?.Id,
        });
    }
}
