using DistributedRag.Shared.Models;
using DistributedRag.Shared.Services;

namespace DistributedRag.Api.Endpoints;

/// <summary>
/// POST /api/query
/// 
/// RAG query endpoint — accepts a URL + question, retrieves relevant chunks
/// via hybrid search (vector + text), merges using Reciprocal Rank Fusion,
/// and generates an LLM-grounded answer via Groq.
/// 
/// Returns the answer plus source chunks for transparency.
/// </summary>
public static class QueryEndpoint
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/api/query", HandleAsync)
            .WithName("Query")
            .WithTags("RAG")
            .Accepts<QueryRequest>("application/json")
            .Produces<QueryResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .WithDescription("Ask a question about a previously processed URL. Returns an LLM-generated answer with source chunks.");
    }

    private static async Task<IResult> HandleAsync(
        QueryRequest request,
        RagQueryService ragService,
        ILogger<QueryRequest> logger)
    {
        // ─── Validate ───
        if (string.IsNullOrWhiteSpace(request.Url))
        {
            return Results.BadRequest(new { error = "URL is required" });
        }

        if (string.IsNullOrWhiteSpace(request.Question))
        {
            return Results.BadRequest(new { error = "Question is required" });
        }

        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            return Results.BadRequest(new { error = "A valid HTTP/HTTPS URL is required" });
        }

        // Normalize URL (same as ProcessUrlEndpoint)
        var normalizedUrl = uri.GetLeftPart(UriPartial.Query).TrimEnd('/');

        logger.LogInformation("Query received — URL: {Url}, Question: {Question}",
            normalizedUrl, request.Question);

        // ─── Execute RAG pipeline ───
        var response = await ragService.QueryAsync(normalizedUrl, request.Question);

        logger.LogInformation("Query answered — {AnswerLength} chars, {SourceCount} sources",
            response.Answer.Length, response.Sources.Count);

        return Results.Ok(response);
    }
}
