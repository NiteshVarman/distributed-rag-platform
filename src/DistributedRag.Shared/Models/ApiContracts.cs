using System.ComponentModel.DataAnnotations;

namespace DistributedRag.Shared.Models;

// ─────────────────────────────────────────────
// REQUEST DTOs
// ─────────────────────────────────────────────

/// <summary>
/// Request body for POST /api/process-url.
/// </summary>
public class ProcessUrlRequest
{
    /// <summary>
    /// The URL to scrape and process for RAG ingestion.
    /// Must be a valid absolute HTTP/HTTPS URL.
    /// </summary>
    [Required(ErrorMessage = "URL is required")]
    [Url(ErrorMessage = "A valid URL is required")]
    public string Url { get; set; } = string.Empty;
}

/// <summary>
/// Request body for POST /api/text-action.
/// Used by the in-page selection toolbar to run a quick LLM action on highlighted text.
/// </summary>
public class TextActionRequest
{
    /// <summary>
    /// The action to perform: summarize | explain | expand | rewrite | grammar | translate | answer.
    /// </summary>
    [Required(ErrorMessage = "Action is required")]
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// The selected text the action operates on.
    /// Capped to protect the LLM context window and control cost.
    /// </summary>
    [Required(ErrorMessage = "Text is required")]
    [MaxLength(10000, ErrorMessage = "Selected text is too long (max 10000 characters)")]
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Target language for the 'translate' action (e.g. "Spanish"). Required only for translate.
    /// </summary>
    public string? TargetLanguage { get; set; }

    /// <summary>
    /// The URL of the page the text was selected on. When provided and the page has
    /// been previously processed, the 'answer' action is grounded in that page via RAG.
    /// </summary>
    public string? PageUrl { get; set; }
}

/// <summary>
/// Request body for POST /api/query.
/// </summary>
public class QueryRequest
{
    /// <summary>
    /// The URL to scope the search to (must have been previously processed).
    /// </summary>
    [Required(ErrorMessage = "URL is required")]
    [Url(ErrorMessage = "A valid URL is required")]
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// The user's natural language question.
    /// </summary>
    [Required(ErrorMessage = "Question is required")]
    [MinLength(3, ErrorMessage = "Question must be at least 3 characters")]
    public string Question { get; set; } = string.Empty;
}

// ─────────────────────────────────────────────
// RESPONSE DTOs
// ─────────────────────────────────────────────

/// <summary>
/// Response for POST /api/process-url — returned immediately after queuing.
/// </summary>
public class ProcessUrlResponse
{
    public string TaskId { get; set; } = string.Empty;
    public string Status { get; set; } = "QUEUED";
    public string Message { get; set; } = "URL queued for processing";
}

/// <summary>
/// Response for GET /api/job-status/{taskId}.
/// </summary>
public class JobStatusResponse
{
    public string TaskId { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Progress { get; set; }
    public string CurrentStep { get; set; } = string.Empty;
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Response for POST /api/query.
/// </summary>
public class QueryResponse
{
    /// <summary>
    /// The LLM-generated answer grounded in the retrieved context.
    /// </summary>
    public string Answer { get; set; } = string.Empty;

    /// <summary>
    /// The source chunks used to generate the answer (for transparency).
    /// </summary>
    public List<SourceChunk> Sources { get; set; } = [];
}

/// <summary>
/// A source chunk returned alongside the LLM answer.
/// </summary>
public class SourceChunk
{
    public string Chunk { get; set; } = string.Empty;
    public string ChunkType { get; set; } = string.Empty;
    public double Score { get; set; }
}

/// <summary>
/// Response for POST /api/text-action.
/// </summary>
public class TextActionResponse
{
    /// <summary>
    /// The LLM-generated result of the action.
    /// </summary>
    public string Result { get; set; } = string.Empty;

    /// <summary>
    /// Echoes the action that was performed.
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// How an 'answer' action was produced: "rag" (grounded in the processed page),
    /// "general" (plain LLM), or "n-a" for non-answer actions.
    /// </summary>
    public string Mode { get; set; } = "n-a";

    /// <summary>
    /// Source chunks, populated only when Mode == "rag".
    /// </summary>
    public List<SourceChunk> Sources { get; set; } = [];
}
