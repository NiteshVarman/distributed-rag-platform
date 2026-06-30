using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DistributedRag.Shared.Models;

/// <summary>
/// Represents a background processing task for URL ingestion.
/// Stored in the 'backgroundTasks' MongoDB collection.
/// </summary>
public class BackgroundTask
{
    /// <summary>
    /// Unique task identifier (GUID string).
    /// </summary>
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The URL being processed.
    /// </summary>
    [BsonElement("url")]
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Current processing status.
    /// </summary>
    [BsonElement("status")]
    [BsonRepresentation(BsonType.String)]
    public TaskProcessingStatus Status { get; set; } = TaskProcessingStatus.QUEUED;

    /// <summary>
    /// Progress percentage (0-100).
    /// </summary>
    [BsonElement("progress")]
    public int Progress { get; set; } = 0;

    /// <summary>
    /// Description of the current processing step.
    /// </summary>
    [BsonElement("currentStep")]
    public string CurrentStep { get; set; } = "Queued for processing";

    /// <summary>
    /// SHA256 hash of the page content (title + cleaned text).
    /// Used to skip re-processing unchanged content.
    /// </summary>
    [BsonElement("contentHash")]
    public string? ContentHash { get; set; }

    /// <summary>
    /// Error message if processing failed.
    /// </summary>
    [BsonElement("error")]
    public string? Error { get; set; }

    /// <summary>
    /// Timestamp when the task was created.
    /// </summary>
    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp of the last update.
    /// </summary>
    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Enum representing the lifecycle states of a processing task.
/// </summary>
public enum TaskProcessingStatus
{
    QUEUED,
    PROCESSING,
    COMPLETED,
    FAILED
}
