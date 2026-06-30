using System.Net;
using System.Text;
using DistributedRag.Functions.Orchestrators;
using DistributedRag.Shared.Models;
using DistributedRag.Shared.Services;
using HtmlAgilityPack;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace DistributedRag.Functions.Activities;

/// <summary>
/// Activity: Structure-aware smart chunking of cleaned HTML content.
/// 
/// Instead of blindly splitting text at fixed character counts, this chunker
/// respects HTML semantic structures:
///   - &lt;p&gt;           → paragraph chunks
///   - &lt;ul&gt;/&lt;ol&gt;      → list chunks
///   - &lt;table&gt;        → table chunks
///   - &lt;pre&gt;/&lt;code&gt;   → code chunks
///   - &lt;h1&gt;-&lt;h6&gt;      → section header + following content
/// 
/// Each chunk type has a token limit. Chunks that exceed the limit
/// are split at sentence boundaries to preserve semantic coherence.
/// </summary>
public class SmartChunkerActivity
{
    private readonly MongoDbService _mongoDb;
    private readonly ILogger<SmartChunkerActivity> _logger;

    // Token limits per chunk type (approximate: 1 token ≈ 4 chars)
    private const int ParagraphTokenLimit = 1000;
    private const int ListTokenLimit = 2000;
    private const int TableTokenLimit = 2000;
    private const int CodeTokenLimit = 1500;
    private const int SectionTokenLimit = 2000;
    private const int DefaultTokenLimit = 1000;

    // Minimum chunk size to avoid noise
    private const int MinChunkChars = 50;

    public SmartChunkerActivity(MongoDbService mongoDb, ILogger<SmartChunkerActivity> logger)
    {
        _mongoDb = mongoDb;
        _logger = logger;
    }

    [Function("SmartChunkerActivity")]
    public async Task<List<ChunkResult>> Run(
        [ActivityTrigger] ChunkerInput input)
    {
        _logger.LogInformation("Smart chunking content for task: {TaskId}", input.TaskId);

        await _mongoDb.UpdateTaskProgressAsync(
            input.TaskId,
            TaskProcessingStatus.PROCESSING,
            progress: 35,
            currentStep: "Chunking content...");

        var chunks = new List<ChunkResult>();

        try
        {
            // Parse the cleaned HTML to extract structural elements
            var doc = new HtmlDocument();
            doc.LoadHtml($"<root>{input.CleanedHtml}</root>");

            var rootNode = doc.DocumentNode.SelectSingleNode("//root") ?? doc.DocumentNode;

            // Walk through top-level nodes and chunk by type. `consumed` tracks sibling
            // nodes already folded into a heading's "section" chunk so they are not
            // emitted a second time as standalone chunks.
            var consumed = new HashSet<HtmlNode>();
            ProcessNode(rootNode, chunks, 0, consumed);

            // Add the page title as the first chunk if meaningful
            if (!string.IsNullOrWhiteSpace(input.Title) && input.Title.Length > 3)
            {
                chunks.Insert(0, new ChunkResult
                {
                    Content = $"Page Title: {input.Title}",
                    ChunkType = "section",
                    Index = 0
                });
            }

            // Re-index all chunks
            for (int i = 0; i < chunks.Count; i++)
            {
                chunks[i].Index = i;
            }

            // Remove any chunks that are too small to be useful
            chunks = chunks.Where(c => c.Content.Length >= MinChunkChars).ToList();

            _logger.LogInformation("Produced {Count} chunks for task {TaskId}", chunks.Count, input.TaskId);

            await _mongoDb.UpdateTaskProgressAsync(
                input.TaskId,
                TaskProcessingStatus.PROCESSING,
                progress: 45,
                currentStep: $"Content chunked into {chunks.Count} pieces");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Chunking failed for task {TaskId}", input.TaskId);
            // Return whatever we have, even if partial
        }

        return chunks;
    }

    /// <summary>
    /// Recursively processes HTML nodes and extracts structure-aware chunks.
    /// </summary>
    private void ProcessNode(HtmlNode node, List<ChunkResult> chunks, int depth, HashSet<HtmlNode> consumed)
    {
        foreach (var child in node.ChildNodes)
        {
            // Skip nodes already folded into a preceding heading's section chunk.
            if (consumed.Contains(child))
                continue;

            var nodeName = child.Name.ToLower();

            switch (nodeName)
            {
                case "p":
                    AddChunks(chunks, GetCleanText(child), "paragraph", ParagraphTokenLimit);
                    break;

                case "ul":
                case "ol":
                    AddChunks(chunks, GetListText(child), "list", ListTokenLimit);
                    break;

                case "table":
                    AddChunks(chunks, GetTableText(child), "table", TableTokenLimit);
                    break;

                case "pre":
                    AddChunks(chunks, GetCleanText(child), "code", CodeTokenLimit);
                    break;

                case "code":
                    // Only treat as code chunk if it's a standalone block (not inline)
                    if (child.ParentNode?.Name.ToLower() != "p" &&
                        child.ParentNode?.Name.ToLower() != "span")
                    {
                        AddChunks(chunks, GetCleanText(child), "code", CodeTokenLimit);
                    }
                    break;

                case "h1":
                case "h2":
                case "h3":
                case "h4":
                case "h5":
                case "h6":
                    // Collect heading + immediately following content as a section.
                    // The consumed siblings are recorded so the main loop skips them.
                    var headingText = GetCleanText(child);
                    var followingText = CollectFollowingSiblingText(child, consumed);
                    var sectionText = string.IsNullOrWhiteSpace(followingText)
                        ? headingText
                        : $"{headingText}\n\n{followingText}";
                    AddChunks(chunks, sectionText, "section", SectionTokenLimit);
                    break;

                case "div":
                case "section":
                case "article":
                case "main":
                case "span":
                    // Recurse into container elements
                    if (depth < 50) // Prevent infinite recursion but allow deep DOMs
                    {
                        ProcessNode(child, chunks, depth + 1, consumed);
                    }
                    break;

                case "blockquote":
                    AddChunks(chunks, GetCleanText(child), "paragraph", ParagraphTokenLimit);
                    break;

                case "#text":
                    // Standalone text nodes (not wrapped in tags)
                    var text = child.InnerText?.Trim();
                    if (!string.IsNullOrWhiteSpace(text) && text.Length >= MinChunkChars)
                    {
                        AddChunks(chunks, text, "paragraph", DefaultTokenLimit);
                    }
                    break;

                default:
                    // For unknown elements, try to extract text
                    var unknownText = GetCleanText(child);
                    if (!string.IsNullOrWhiteSpace(unknownText) && unknownText.Length >= MinChunkChars)
                    {
                        AddChunks(chunks, unknownText, "paragraph", DefaultTokenLimit);
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Adds one or more chunks, splitting at sentence boundaries if the text exceeds the token limit.
    /// </summary>
    private void AddChunks(List<ChunkResult> chunks, string text, string chunkType, int tokenLimit)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        text = text.Trim();
        var charLimit = tokenLimit * 4; // Approximate: 1 token ≈ 4 chars

        if (text.Length <= charLimit)
        {
            chunks.Add(new ChunkResult
            {
                Content = text,
                ChunkType = chunkType,
                Index = chunks.Count
            });
            return;
        }

        // Split at sentence boundaries
        var sentences = SplitIntoSentences(text);
        var current = new StringBuilder();

        foreach (var sentence in sentences)
        {
            if (current.Length + sentence.Length > charLimit && current.Length > 0)
            {
                chunks.Add(new ChunkResult
                {
                    Content = current.ToString().Trim(),
                    ChunkType = chunkType,
                    Index = chunks.Count
                });
                current.Clear();
            }
            current.Append(sentence);
        }

        // Add remaining content
        if (current.Length > 0)
        {
            chunks.Add(new ChunkResult
            {
                Content = current.ToString().Trim(),
                ChunkType = chunkType,
                Index = chunks.Count
            });
        }
    }

    /// <summary>
    /// Extracts clean text from an HTML node, decoding entities.
    /// </summary>
    private static string GetCleanText(HtmlNode node)
    {
        var text = node.InnerText ?? "";
        text = WebUtility.HtmlDecode(text);
        // Collapse whitespace
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
        return text.Trim();
    }

    /// <summary>
    /// Extracts formatted text from a list element (ul/ol), preserving bullet structure.
    /// </summary>
    private static string GetListText(HtmlNode listNode)
    {
        var sb = new StringBuilder();
        var items = listNode.SelectNodes(".//li");
        if (items is null) return GetCleanText(listNode);

        for (int i = 0; i < items.Count; i++)
        {
            var prefix = listNode.Name.ToLower() == "ol" ? $"{i + 1}." : "•";
            sb.AppendLine($"{prefix} {GetCleanText(items[i])}");
        }
        return sb.ToString().Trim();
    }

    /// <summary>
    /// Extracts text from a table element, preserving row/column structure.
    /// </summary>
    private static string GetTableText(HtmlNode tableNode)
    {
        var sb = new StringBuilder();
        var rows = tableNode.SelectNodes(".//tr");
        if (rows is null) return GetCleanText(tableNode);

        foreach (var row in rows)
        {
            var cells = row.SelectNodes(".//td|.//th");
            if (cells is null) continue;

            var cellTexts = cells.Select(c => GetCleanText(c));
            sb.AppendLine(string.Join(" | ", cellTexts));
        }
        return sb.ToString().Trim();
    }

    /// <summary>
    /// Gets the text of immediately following sibling nodes until the next heading,
    /// recording each consumed sibling in <paramref name="consumed"/> so the caller's
    /// loop does not emit them again as standalone chunks.
    /// Used to group heading + content as a section chunk.
    /// </summary>
    private static string CollectFollowingSiblingText(HtmlNode headingNode, HashSet<HtmlNode> consumed)
    {
        var sb = new StringBuilder();
        var sibling = headingNode.NextSibling;
        var headingNames = new HashSet<string> { "h1", "h2", "h3", "h4", "h5", "h6" };
        var maxChars = 2000; // Limit how much we collect

        while (sibling != null && sb.Length < maxChars)
        {
            var name = sibling.Name.ToLower();

            // Stop at the next heading
            if (headingNames.Contains(name))
                break;

            var text = GetCleanText(sibling);
            if (!string.IsNullOrWhiteSpace(text))
            {
                sb.AppendLine(text);
            }

            // Mark this sibling as consumed even if it was whitespace-only, so it is
            // never re-processed by the main loop.
            consumed.Add(sibling);
            sibling = sibling.NextSibling;
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Splits text into sentences at sentence-ending punctuation.
    /// </summary>
    private static List<string> SplitIntoSentences(string text)
    {
        var sentences = new List<string>();
        var pattern = @"(?<=[.!?])\s+";
        var parts = System.Text.RegularExpressions.Regex.Split(text, pattern);

        foreach (var part in parts)
        {
            if (!string.IsNullOrWhiteSpace(part))
            {
                sentences.Add(part + " ");
            }
        }

        return sentences;
    }
}
