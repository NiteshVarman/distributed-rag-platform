using System.Text;
using System.Text.Json;
using DistributedRag.Shared.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DistributedRag.Shared.Services;

/// <summary>
/// Generates text embeddings via the Google Gemini API (free tier).
///
/// Model: text-embedding-004 → 768-dimensional vectors (must match the MongoDB
/// Atlas vector_index numDimensions). Uses :batchEmbedContents so a batch of
/// chunks is embedded in a single request.
///
/// Endpoint: https://generativelanguage.googleapis.com/v1beta/models/{model}:batchEmbedContents?key=...
/// </summary>
public class GeminiEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly GeminiSettings _settings;
    private readonly ILogger<GeminiEmbeddingService> _logger;

    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta";

    public GeminiEmbeddingService(
        IHttpClientFactory httpClientFactory,
        IOptions<GeminiSettings> settings,
        ILogger<GeminiEmbeddingService> logger)
    {
        _settings = settings.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
            throw new InvalidOperationException(
                "Gemini API key is not configured. Set 'Gemini:ApiKey' (or Gemini__ApiKey) in configuration.");

        _httpClient = httpClientFactory.CreateClient("Gemini");
        _httpClient.Timeout = TimeSpan.FromSeconds(60);

        _logger.LogInformation(
            "GeminiEmbeddingService initialized — Model: {Model}, Dimensions: {Dims}",
            _settings.EmbeddingModel, _settings.EmbeddingDimensions);
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text)
    {
        var results = await GenerateEmbeddingsAsync([text]);
        return results[0];
    }

    public async Task<List<float[]>> GenerateEmbeddingsAsync(List<string> texts)
    {
        if (texts.Count == 0) return [];

        var modelPath = _settings.EmbeddingModel.StartsWith("models/")
            ? _settings.EmbeddingModel
            : $"models/{_settings.EmbeddingModel}";
        var url = $"{BaseUrl}/{modelPath}:batchEmbedContents?key={_settings.ApiKey}";

        var requestBody = new
        {
            requests = texts.Select(t => new
            {
                model = modelPath,
                content = new { parts = new[] { new { text = t } } }
            }).ToArray()
        };
        var json = JsonSerializer.Serialize(requestBody);

        _logger.LogInformation("Requesting embeddings for {Count} text(s) from Gemini ({Model})",
            texts.Count, _settings.EmbeddingModel);

        var maxRetries = 3;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content);

            if (response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync();
                var embeddings = ParseEmbeddingsResponse(responseBody);

                _logger.LogInformation(
                    "Generated {Count} embeddings ({Dims} dims each)",
                    embeddings.Count, embeddings.FirstOrDefault()?.Length ?? 0);

                return embeddings;
            }

            // Retry on rate limit / transient server errors.
            if (((int)response.StatusCode == 429 || (int)response.StatusCode >= 500) && attempt < maxRetries)
            {
                var wait = TimeSpan.FromSeconds(2 * attempt);
                _logger.LogWarning("Gemini API {Status} — retrying in {Wait}s (attempt {Attempt}/{Max})",
                    (int)response.StatusCode, wait.TotalSeconds, attempt, maxRetries);
                await Task.Delay(wait);
                continue;
            }

            var errorBody = await response.Content.ReadAsStringAsync();
            var errorMsg = $"Gemini API error: HTTP {(int)response.StatusCode} — {errorBody}";
            _logger.LogError(errorMsg);
            throw new HttpRequestException(errorMsg);
        }

        throw new HttpRequestException("Gemini API: max retries exceeded");
    }

    /// <summary>
    /// Parses { "embeddings": [ { "values": [..] }, ... ] }.
    /// </summary>
    private List<float[]> ParseEmbeddingsResponse(string responseBody)
    {
        using var doc = JsonDocument.Parse(responseBody);
        var embeddings = new List<float[]>();

        if (doc.RootElement.TryGetProperty("embeddings", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in arr.EnumerateArray())
            {
                if (item.TryGetProperty("values", out var vals) && vals.ValueKind == JsonValueKind.Array)
                {
                    embeddings.Add(vals.EnumerateArray().Select(v => v.GetSingle()).ToArray());
                }
            }
        }

        foreach (var embedding in embeddings)
        {
            if (embedding.Length != _settings.EmbeddingDimensions)
            {
                _logger.LogWarning(
                    "Unexpected embedding dimensions: {Actual} (expected {Expected})",
                    embedding.Length, _settings.EmbeddingDimensions);
            }
        }

        return embeddings;
    }
}
