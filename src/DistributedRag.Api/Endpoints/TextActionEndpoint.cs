using DistributedRag.Shared.Models;
using DistributedRag.Shared.Services;

namespace DistributedRag.Api.Endpoints;

/// <summary>
/// POST /api/text-action
///
/// Powers the in-page selection toolbar. Runs a quick LLM action (summarize,
/// explain, expand, rewrite, grammar, translate) on the highlighted text, or
/// answers a selected question.
///
/// The 'answer' action is grounded in the current page via RAG when that page has
/// already been ingested; otherwise it falls back to a plain LLM answer. The chosen
/// mode is reported back so the UI can show a "from this page" / "general" badge.
/// </summary>
public static class TextActionEndpoint
{
    // Single source of truth: action → system prompt. {0} is replaced for translate.
    private static readonly Dictionary<string, string> ActionPrompts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["summarize"] = "You summarize text. Summarize the following text concisely in 2-4 sentences. Output only the summary, with no preamble.",
        ["explain"] = "You explain text. Explain the following text in simple, clear language for a general audience. Output only the explanation.",
        ["expand"] = "You expand text. Expand the following text with more detail and helpful context, preserving its original meaning and tone. Output only the expanded text.",
        ["rewrite"] = "You rewrite text. Rewrite the following text to be clearer and more fluent while keeping the original meaning and language. Output only the rewritten text.",
        ["grammar"] = "You are a proofreader. Correct the grammar, spelling, and punctuation of the following text. Keep the original meaning and language. Output only the corrected text, with no commentary.",
    };

    private const string TranslatePromptTemplate =
        "You are a translator. Translate the following text into {0}. Preserve meaning and tone. Output only the translation, with no preamble.";

    private const string GeneralAnswerPrompt =
        "You are a helpful assistant. Answer the user's question clearly and concisely.";

    public static void Map(WebApplication app)
    {
        app.MapPost("/api/text-action", HandleAsync)
            .WithName("TextAction")
            .WithTags("Text")
            .Accepts<TextActionRequest>("application/json")
            .Produces<TextActionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .WithDescription("Run a quick LLM action (summarize, explain, expand, rewrite, grammar, translate, answer) on selected text.");
    }

    private static async Task<IResult> HandleAsync(
        TextActionRequest request,
        GroqLlmService groq,
        RagQueryService ragService,
        MongoDbService mongoDb,
        ILogger<TextActionRequest> logger)
    {
        // ─── Validate ───
        if (string.IsNullOrWhiteSpace(request.Action))
            return Results.BadRequest(new { error = "Action is required" });

        if (string.IsNullOrWhiteSpace(request.Text))
            return Results.BadRequest(new { error = "Text is required" });

        if (request.Text.Length > 10000)
            return Results.BadRequest(new { error = "Selected text is too long (max 10000 characters)" });

        var action = request.Action.Trim().ToLowerInvariant();
        logger.LogInformation("Text action '{Action}' — {Length} chars", action, request.Text.Length);

        // ─── Answer: RAG-if-processed, else general ───
        if (action is "answer" or "answer-question")
        {
            return await HandleAnswerAsync(request, groq, ragService, mongoDb, logger);
        }

        // ─── Translate: needs a target language ───
        if (action == "translate")
        {
            if (string.IsNullOrWhiteSpace(request.TargetLanguage))
                return Results.BadRequest(new { error = "TargetLanguage is required for the translate action" });

            var translatePrompt = string.Format(TranslatePromptTemplate, request.TargetLanguage.Trim());
            return await RunSimpleActionAsync(action, translatePrompt, request.Text, groq, logger);
        }

        // ─── Other generic text actions ───
        if (ActionPrompts.TryGetValue(action, out var systemPrompt))
        {
            return await RunSimpleActionAsync(action, systemPrompt, request.Text, groq, logger);
        }

        return Results.BadRequest(new { error = $"Unknown action '{request.Action}'" });
    }

    /// <summary>
    /// Runs a single-shot LLM action and wraps the result.
    /// </summary>
    private static async Task<IResult> RunSimpleActionAsync(
        string action,
        string systemPrompt,
        string text,
        GroqLlmService groq,
        ILogger logger)
    {
        try
        {
            var result = await groq.SendChatCompletionAsync(systemPrompt, text);
            return Results.Ok(new TextActionResponse { Result = result, Action = action, Mode = "n-a" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Text action '{Action}' failed", action);
            return Results.Problem(
                detail: "The AI service is unavailable right now. Please try again.",
                statusCode: StatusCodes.Status502BadGateway);
        }
    }

    /// <summary>
    /// Detects RagQueryService's "can't answer from this page" responses so the caller
    /// can fall back to a general LLM answer. Matches the sentinels produced by
    /// RagQueryService (empty corpus) and the grounded system prompt (LLM refusal).
    /// </summary>
    private static bool IsNoAnswer(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer)) return true;
        var a = answer.TrimStart();
        return a.StartsWith("I don't have enough information", StringComparison.OrdinalIgnoreCase)
            || a.StartsWith("I couldn't find any processed content", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Answer action: pre-checks whether the page was processed (a single indexed lookup)
    /// and routes to grounded RAG or a plain LLM answer accordingly.
    /// </summary>
    private static async Task<IResult> HandleAnswerAsync(
        TextActionRequest request,
        GroqLlmService groq,
        RagQueryService ragService,
        MongoDbService mongoDb,
        ILogger logger)
    {
        // No page context → general answer.
        if (!string.IsNullOrWhiteSpace(request.PageUrl)
            && Uri.TryCreate(request.PageUrl, UriKind.Absolute, out var uri)
            && (uri.Scheme == "http" || uri.Scheme == "https"))
        {
            // Normalize identically to ProcessUrlEndpoint / QueryEndpoint so lookups match.
            var normalizedUrl = uri.GetLeftPart(UriPartial.Query).TrimEnd('/');

            // Pre-check: a non-null hash means a COMPLETED ingest (and embeddings) exist.
            string? existingHash;
            try
            {
                existingHash = await mongoDb.GetExistingContentHashForUrlAsync(normalizedUrl);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Processed-page check failed for {Url} — using general answer", normalizedUrl);
                existingHash = null;
            }

            if (existingHash != null)
            {
                logger.LogInformation("Answer grounded in processed page: {Url}", normalizedUrl);
                var rag = await ragService.QueryAsync(normalizedUrl, request.Text);

                // If the page genuinely covers the question, return the grounded answer.
                // Otherwise fall through to a general LLM answer so the user isn't dead-ended.
                if (!IsNoAnswer(rag.Answer))
                {
                    return Results.Ok(new TextActionResponse
                    {
                        Result = rag.Answer,
                        Action = "answer",
                        Mode = "rag",
                        Sources = rag.Sources
                    });
                }

                logger.LogInformation(
                    "RAG had no grounded answer for {Url} — falling back to general LLM", normalizedUrl);
            }
        }

        // Fall back to a general LLM answer.
        try
        {
            var answer = await groq.SendChatCompletionAsync(GeneralAnswerPrompt, request.Text);
            return Results.Ok(new TextActionResponse { Result = answer, Action = "answer", Mode = "general" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "General answer failed");
            return Results.Problem(
                detail: "The AI service is unavailable right now. Please try again.",
                statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
