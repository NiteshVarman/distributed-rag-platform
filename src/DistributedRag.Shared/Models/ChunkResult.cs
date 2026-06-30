namespace DistributedRag.Shared.Models;

/// <summary>
/// Represents a processed text chunk from the smart chunker.
/// Passed from SmartChunkerActivity → EmbedderActivity.
/// </summary>
public class ChunkResult
{
    /// <summary>
    /// The text content of the chunk.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// The type of HTML structure this chunk was extracted from.
    /// Values: "paragraph", "list", "table", "code", "section"
    /// </summary>
    public string ChunkType { get; set; } = "paragraph";

    /// <summary>
    /// Zero-based index of this chunk within the document.
    /// Used for ordering and fan-out parallelism.
    /// </summary>
    public int Index { get; set; }
}
