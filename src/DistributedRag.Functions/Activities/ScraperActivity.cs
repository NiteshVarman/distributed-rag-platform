using System.Net;
using DistributedRag.Functions.Orchestrators;
using DistributedRag.Shared.Models;
using DistributedRag.Shared.Services;
using HtmlAgilityPack;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace DistributedRag.Functions.Activities;

/// <summary>
/// Activity: Fetches HTML from a URL and extracts clean structured text.
/// 
/// Uses HttpClient to fetch the page and HtmlAgilityPack to parse/clean the HTML.
/// Removes script, style, img, nav, footer, and other non-content elements.
/// Preserves semantic structure (paragraphs, lists, tables, code blocks) for the Smart Chunker.
/// </summary>
public class ScraperActivity
{
    // Cap the amount of HTML we read into memory to protect the worker from
    // hostile or accidentally huge pages.
    private const long MaxContentBytes = 10 * 1024 * 1024; // 10 MB

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly MongoDbService _mongoDb;
    private readonly ILogger<ScraperActivity> _logger;

    public ScraperActivity(
        IHttpClientFactory httpClientFactory,
        MongoDbService mongoDb,
        ILogger<ScraperActivity> logger)
    {
        _httpClientFactory = httpClientFactory;
        _mongoDb = mongoDb;
        _logger = logger;
    }

    [Function("ScraperActivity")]
    public async Task<ScraperResult> Run(
        [ActivityTrigger] ScraperInput input)
    {
        _logger.LogInformation("Scraping URL: {Url}", input.Url);

        // Update task progress
        await _mongoDb.UpdateTaskProgressAsync(
            input.TaskId,
            TaskProcessingStatus.PROCESSING,
            progress: 10,
            currentStep: "Fetching web page...");

        try
        {
            // The "Scraper" client has auto-redirect disabled so we follow redirects
            // ourselves and SSRF-check every hop (auto-redirect would bypass that, and
            // also refuses HTTPS→HTTP downgrades — which surface as an unhandled 301).
            var client = _httpClientFactory.CreateClient("Scraper");
            client.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            client.Timeout = TimeSpan.FromSeconds(30);

            const int maxRedirects = 5;
            var currentUrl = input.Url;
            HttpResponseMessage? response = null;

            for (int hop = 0; hop <= maxRedirects; hop++)
            {
                // ─── SSRF guard: re-check every hop (a safe URL can redirect to an internal one) ───
                var (safe, reason) = await UrlSafetyValidator.ValidateAsync(currentUrl);
                if (!safe)
                {
                    _logger.LogWarning("Refusing to fetch unsafe URL {Url}: {Reason}", currentUrl, reason);
                    return new ScraperResult { Success = false, Error = $"URL rejected: {reason}" };
                }

                // Stream headers first so we can enforce the size cap before buffering the body.
                response = await client.GetAsync(currentUrl, HttpCompletionOption.ResponseHeadersRead);

                if (!IsRedirect(response.StatusCode))
                    break;

                var location = response.Headers.Location;
                if (location is null)
                {
                    var error = $"HTTP {(int)response.StatusCode} with no Location header for {currentUrl}";
                    _logger.LogError(error);
                    return new ScraperResult { Success = false, Error = error };
                }

                // Resolve relative redirects (e.g. "/new-path") against the current URL.
                var next = location.IsAbsoluteUri ? location : new Uri(new Uri(currentUrl), location);
                _logger.LogInformation("Following {Status} redirect: {From} → {To}",
                    (int)response.StatusCode, currentUrl, next);
                currentUrl = next.ToString();
                response.Dispose();
                response = null;
            }

            if (response is null)
            {
                return new ScraperResult { Success = false, Error = $"Too many redirects (>{maxRedirects}) for {input.Url}" };
            }

            if (!response.IsSuccessStatusCode)
            {
                var error = $"HTTP {(int)response.StatusCode} {response.StatusCode} when fetching {currentUrl}";
                _logger.LogError(error);
                return new ScraperResult { Success = false, Error = error };
            }

            if (response.Content.Headers.ContentLength is long declared && declared > MaxContentBytes)
            {
                return new ScraperResult
                {
                    Success = false,
                    Error = $"Content too large ({declared} bytes, limit {MaxContentBytes})."
                };
            }

            var html = await ReadCappedStringAsync(response);
            if (html is null)
            {
                return new ScraperResult
                {
                    Success = false,
                    Error = $"Content exceeded the {MaxContentBytes}-byte limit."
                };
            }

            if (string.IsNullOrWhiteSpace(html))
            {
                return new ScraperResult { Success = false, Error = "Empty response from URL" };
            }

            _logger.LogInformation("Fetched {Length} chars of HTML from {Url}", html.Length, input.Url);

            // ─── Parse with HtmlAgilityPack ───
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Extract title
            var title = doc.DocumentNode
                .SelectSingleNode("//title")?.InnerText?.Trim()
                ?? doc.DocumentNode
                    .SelectSingleNode("//h1")?.InnerText?.Trim()
                ?? "Untitled";

            title = WebUtility.HtmlDecode(title);

            // Remove unwanted elements
            RemoveNodes(doc, "//script");
            RemoveNodes(doc, "//style");
            RemoveNodes(doc, "//img");
            RemoveNodes(doc, "//svg");
            RemoveNodes(doc, "//video");
            RemoveNodes(doc, "//audio");
            RemoveNodes(doc, "//iframe");
            RemoveNodes(doc, "//nav");
            RemoveNodes(doc, "//footer");
            RemoveNodes(doc, "//header");
            RemoveNodes(doc, "//aside");
            RemoveNodes(doc, "//noscript");
            RemoveNodes(doc, "//form");
            RemoveNodes(doc, "//button");
            RemoveNodes(doc, "//input");

            // Get the body content (or full document if no body)
            var bodyNode = doc.DocumentNode.SelectSingleNode("//body")
                ?? doc.DocumentNode.SelectSingleNode("//main")
                ?? doc.DocumentNode;

            // Get the cleaned HTML (preserving structural tags for the chunker)
            var cleanedHtml = bodyNode.InnerHtml;

            // Decode HTML entities
            cleanedHtml = WebUtility.HtmlDecode(cleanedHtml);

            // Collapse excessive whitespace but preserve newlines
            cleanedHtml = System.Text.RegularExpressions.Regex.Replace(cleanedHtml, @"[ \t]+", " ");
            cleanedHtml = System.Text.RegularExpressions.Regex.Replace(cleanedHtml, @"\n{3,}", "\n\n");
            cleanedHtml = cleanedHtml.Trim();

            if (string.IsNullOrWhiteSpace(cleanedHtml))
            {
                return new ScraperResult { Success = false, Error = "No content found after cleaning HTML" };
            }

            _logger.LogInformation("Cleaned HTML: {Length} chars, Title: {Title}", cleanedHtml.Length, title);

            // Update progress
            await _mongoDb.UpdateTaskProgressAsync(
                input.TaskId,
                TaskProcessingStatus.PROCESSING,
                progress: 25,
                currentStep: "Web page fetched and cleaned");

            return new ScraperResult
            {
                Success = true,
                Title = title,
                CleanedHtml = cleanedHtml
            };
        }
        catch (TaskCanceledException)
        {
            return new ScraperResult { Success = false, Error = $"Timeout fetching URL: {input.Url}" };
        }
        catch (HttpRequestException ex)
        {
            return new ScraperResult { Success = false, Error = $"Network error: {ex.Message}" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scraper failed for URL: {Url}", input.Url);
            return new ScraperResult { Success = false, Error = $"Scraping failed: {ex.Message}" };
        }
    }

    /// <summary>
    /// Redirect status codes we follow manually (301/302/303/307/308).
    /// </summary>
    private static bool IsRedirect(HttpStatusCode code) =>
        code is HttpStatusCode.MovedPermanently   // 301
            or HttpStatusCode.Found               // 302
            or HttpStatusCode.SeeOther            // 303
            or HttpStatusCode.TemporaryRedirect   // 307
            or HttpStatusCode.PermanentRedirect;  // 308

    /// <summary>
    /// Reads the response body as a string while enforcing <see cref="MaxContentBytes"/>.
    /// Returns null if the body exceeds the cap (servers can lie about / omit Content-Length).
    /// </summary>
    private static async Task<string?> ReadCappedStringAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var buffer = new MemoryStream();

        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk)) > 0)
        {
            if (buffer.Length + read > MaxContentBytes)
                return null;
            buffer.Write(chunk, 0, read);
        }

        var encoding = System.Text.Encoding.UTF8;
        return encoding.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    /// <summary>
    /// Removes all nodes matching the given XPath expression.
    /// </summary>
    private static void RemoveNodes(HtmlDocument doc, string xpath)
    {
        var nodes = doc.DocumentNode.SelectNodes(xpath);
        if (nodes is null) return;

        foreach (var node in nodes.ToList())
        {
            node.Remove();
        }
    }
}
