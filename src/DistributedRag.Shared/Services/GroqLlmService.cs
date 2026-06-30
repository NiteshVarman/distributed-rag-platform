using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DistributedRag.Shared.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DistributedRag.Shared.Services;

/// <summary>
/// Service for generating LLM responses via the Groq API.
/// 
/// Groq uses an OpenAI-compatible API format, making it easy to switch
/// between providers if needed.
/// 
/// API endpoint: https://api.groq.com/openai/v1/chat/completions
/// Model: llama-3.3-70b-versatile (default)
/// 
/// Registered as a singleton — HttpClient is managed via IHttpClientFactory.
/// </summary>
public class GroqLlmService
{
    private readonly HttpClient _httpClient;
    private readonly GroqSettings _settings;
    private readonly ILogger<GroqLlmService> _logger;

    private const string ApiUrl = "https://api.groq.com/openai/v1/chat/completions";

    public GroqLlmService(
        IHttpClientFactory httpClientFactory,
        IOptions<GroqSettings> settings,
        ILogger<GroqLlmService> logger)
    {
        _settings = settings.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
            throw new InvalidOperationException(
                "Groq API key is not configured. " +
                "Set 'Groq:ApiKey' in appsettings.json or environment variables.");

        _httpClient = httpClientFactory.CreateClient("Groq");
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _settings.ApiKey);
        _httpClient.Timeout = TimeSpan.FromSeconds(30);

        _logger.LogInformation("GroqLlmService initialized — Model: {Model}", _settings.Model);
    }

    /// <summary>
    /// Generates a RAG-grounded answer by sending the context chunks and user question
    /// to the Groq LLM with a strict system prompt.
    /// </summary>
    /// <param name="contextChunks">Retrieved text chunks to use as context.</param>
    /// <param name="question">The user's natural language question.</param>
    /// <returns>The LLM-generated answer.</returns>
    public async Task<string> GenerateAnswerAsync(List<string> contextChunks, string question)
    {
        var context = string.Join("\n\n---\n\n", contextChunks);

        var systemPrompt = """
            You are a helpful assistant that answers questions based ONLY on the provided context.

            Rules:
            1. Answer ONLY using the information in the context below.
            2. If the user provides a topic or statement instead of a question, summarize the relevant information about that topic from the context.
            3. If the information cannot be found in the context, say "I don't have enough information to answer that question based on the available content."
            4. Be concise and direct in your answers.
            5. If the context contains relevant code examples, include them in your answer.
            6. Cite which part of the context your answer is based on when possible.

            Context:
            """ + context;

        return await SendChatCompletionAsync(systemPrompt, question);
    }

    /// <summary>
    /// Sends a chat completion request to the Groq API.
    /// Uses the OpenAI-compatible format with system + user messages.
    /// </summary>
    public async Task<string> SendChatCompletionAsync(string systemMessage, string userMessage)
    {
        var requestBody = new
        {
            model = _settings.Model,
            messages = new[]
            {
                new { role = "system", content = systemMessage },
                new { role = "user", content = userMessage }
            },
            max_tokens = _settings.MaxTokens,
            temperature = _settings.Temperature,
            top_p = 1,
            stream = false
        };

        var json = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogInformation(
            "Sending chat completion — Model: {Model}, Question length: {Length}",
            _settings.Model, userMessage.Length);

        var maxRetries = 3;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            var response = await _httpClient.PostAsync(ApiUrl, content);

            if (response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync();
                var answer = ExtractAnswerFromResponse(responseBody);

                _logger.LogInformation("LLM response generated — {Length} chars", answer.Length);
                return answer;
            }

            // Handle rate limiting (429)
            if ((int)response.StatusCode == 429)
            {
                _logger.LogWarning("Groq rate limited (429), attempt {Attempt}/{Max}", attempt, maxRetries);

                if (attempt < maxRetries)
                {
                    // Check for Retry-After header
                    var retryAfter = response.Headers.RetryAfter?.Delta?.TotalSeconds ?? (5 * attempt);
                    _logger.LogInformation("Waiting {Seconds}s before retry...", retryAfter);
                    await Task.Delay(TimeSpan.FromSeconds(retryAfter));

                    // Recreate content since it was consumed
                    content = new StringContent(json, Encoding.UTF8, "application/json");
                    continue;
                }
            }

            // Handle server errors (503, 500)
            if ((int)response.StatusCode >= 500)
            {
                _logger.LogWarning("Groq server error ({Status}), attempt {Attempt}/{Max}",
                    (int)response.StatusCode, attempt, maxRetries);

                if (attempt < maxRetries)
                {
                    await Task.Delay(TimeSpan.FromSeconds(3 * attempt));
                    content = new StringContent(json, Encoding.UTF8, "application/json");
                    continue;
                }
            }

            // Other errors — throw immediately
            var errorBody = await response.Content.ReadAsStringAsync();
            var errorMsg = $"Groq API error: HTTP {(int)response.StatusCode} — {errorBody}";
            _logger.LogError(errorMsg);
            throw new HttpRequestException(errorMsg);
        }

        throw new HttpRequestException("Groq API: max retries exceeded");
    }

    /// <summary>
    /// Extracts the assistant's message content from the Groq API response.
    /// 
    /// Response format (OpenAI-compatible):
    /// {
    ///   "choices": [
    ///     {
    ///       "message": {
    ///         "role": "assistant",
    ///         "content": "The answer..."
    ///       }
    ///     }
    ///   ]
    /// }
    /// </summary>
    private string ExtractAnswerFromResponse(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var choices = doc.RootElement.GetProperty("choices");

            if (choices.GetArrayLength() == 0)
            {
                _logger.LogWarning("Groq response contained no choices");
                return "No response generated.";
            }

            var message = choices[0].GetProperty("message");
            var answer = message.GetProperty("content").GetString() ?? "No response generated.";

            // Log token usage if available
            if (doc.RootElement.TryGetProperty("usage", out var usage))
            {
                var promptTokens = usage.GetProperty("prompt_tokens").GetInt32();
                var completionTokens = usage.GetProperty("completion_tokens").GetInt32();
                _logger.LogInformation(
                    "Token usage — Prompt: {Prompt}, Completion: {Completion}, Total: {Total}",
                    promptTokens, completionTokens, promptTokens + completionTokens);
            }

            return answer;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse Groq response: {Body}", responseBody);
            return "Failed to parse LLM response.";
        }
    }
}
