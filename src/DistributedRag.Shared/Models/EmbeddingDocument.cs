using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DistributedRag.Shared.Models;

/// <summary>
/// Represents a text chunk with its vector embedding.
/// Stored in the 'embeddings' MongoDB collection.
/// Used for vector search and text search in the RAG query pipeline.
/// </summary>
public class EmbeddingDocument
{
    /// <summary>
    /// MongoDB ObjectId (auto-generated).
    /// </summary>
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    /// <summary>
    /// Reference to the parent BackgroundTask that created this embedding.
    /// </summary>
    [BsonElement("taskId")]
    public string TaskId { get; set; } = string.Empty;

    /// <summary>
    /// The source URL this chunk was extracted from.
    /// </summary>
    [BsonElement("url")]
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// The text chunk content.
    /// </summary>
    [BsonElement("chunk")]
    public string Chunk { get; set; } = string.Empty;

    /// <summary>
    /// The type of content this chunk represents (paragraph, list, table, code, section).
    /// </summary>
    [BsonElement("chunkType")]
    public string ChunkType { get; set; } = "paragraph";

    /// <summary>
    /// Zero-based index of this chunk within the document.
    /// Part of the idempotency key (url + contentHash + chunkIndex) so that a
    /// retried EmbedderActivity upserts the same document instead of inserting a duplicate.
    /// </summary>
    [BsonElement("chunkIndex")]
    public int ChunkIndex { get; set; }

    /// <summary>
    /// Vector embedding (384 dimensions for BAAI/bge-small-en-v1.5).
    /// </summary>
    [BsonElement("embedding")]
    public float[] Embedding { get; set; } = [];

    /// <summary>
    /// SHA256 content hash — matches the parent task's content hash.
    /// Used for idempotent storage and deduplication.
    /// </summary>
    [BsonElement("contentHash")]
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when this embedding was created.
    /// </summary>
    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
