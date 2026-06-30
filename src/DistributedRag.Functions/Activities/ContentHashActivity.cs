using System.Security.Cryptography;
using System.Text;
using DistributedRag.Functions.Orchestrators;
using DistributedRag.Shared.Models;
using DistributedRag.Shared.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace DistributedRag.Functions.Activities;

/// <summary>
/// Activity: Computes a SHA256 content hash and checks for deduplication.
/// 
/// Hash = SHA256(title + normalized cleaned content)
/// 
/// If a previously completed task for the same URL has the same hash,
/// embedding generation is skipped entirely. This prevents redundant
/// processing when re-submitting an unchanged URL.
/// </summary>
public class ContentHashActivity
{
    private readonly MongoDbService _mongoDb;
    private readonly ILogger<ContentHashActivity> _logger;

    public ContentHashActivity(MongoDbService mongoDb, ILogger<ContentHashActivity> logger)
    {
        _mongoDb = mongoDb;
        _logger = logger;
    }

    [Function("ContentHashActivity")]
    public async Task<ContentHashResult> Run(
        [ActivityTrigger] ContentHashInput input)
    {
        _logger.LogInformation("Computing content hash for task {TaskId}", input.TaskId);

        // Compute SHA256 hash of title + content
        var contentToHash = $"{input.Title}\n{NormalizeContent(input.CleanedContent)}";
        var hash = ComputeSha256Hash(contentToHash);

        _logger.LogInformation("Content hash: {Hash}", hash[..16] + "...");

        // Store the hash on the task
        await _mongoDb.SetTaskContentHashAsync(input.TaskId, hash);

        // Check if this URL was previously processed with the same hash
        var existingHash = await _mongoDb.GetExistingContentHashForUrlAsync(input.Url);

        if (existingHash != null && existingHash == hash)
        {
            _logger.LogInformation(
                "Content unchanged for URL {Url} — skipping embedding generation", input.Url);

            await _mongoDb.UpdateTaskProgressAsync(
                input.TaskId,
                TaskProcessingStatus.PROCESSING,
                progress: 90,
                currentStep: "Content unchanged — skipping re-processing");

            return new ContentHashResult
            {
                ContentHash = hash,
                IsUnchanged = true
            };
        }

        // Content changed (or first time). Old embeddings (if any) are NOT deleted
        // here — they are removed in FinalizeActivity AFTER the new embeddings are
        // written, so a concurrent query never sees the URL with zero content.
        if (existingHash != null)
        {
            _logger.LogInformation(
                "Content changed for URL {Url} — new embeddings will replace old ones at finalize", input.Url);
        }

        await _mongoDb.UpdateTaskProgressAsync(
            input.TaskId,
            TaskProcessingStatus.PROCESSING,
            progress: 50,
            currentStep: "Content hash computed — ready for embedding");

        return new ContentHashResult
        {
            ContentHash = hash,
            IsUnchanged = false
        };
    }

    /// <summary>
    /// Normalizes content for consistent hashing (lowercase, collapse whitespace).
    /// </summary>
    private static string NormalizeContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return "";

        var normalized = content.ToLowerInvariant();
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\s+", " ");
        return normalized.Trim();
    }

    /// <summary>
    /// Computes the SHA256 hash of a string, returning it as a hex string.
    /// </summary>
    private static string ComputeSha256Hash(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
