using DistributedRag.Shared.Configuration;
using DistributedRag.Shared.Services;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

// ─────────────────────────────────────────────
// Configuration Binding
// ─────────────────────────────────────────────
builder.Services.Configure<MongoDbSettings>(
    builder.Configuration.GetSection(MongoDbSettings.SectionName));
builder.Services.Configure<ServiceBusSettings>(
    builder.Configuration.GetSection(ServiceBusSettings.SectionName));
builder.Services.Configure<GeminiSettings>(
    builder.Configuration.GetSection(GeminiSettings.SectionName));

// ─────────────────────────────────────────────
// Services — Singleton (thread-safe, connection-pooled)
// ─────────────────────────────────────────────
builder.Services.AddSingleton<MongoDbService>();
builder.Services.AddSingleton<IEmbeddingService, GeminiEmbeddingService>();
builder.Services.AddHttpClient(); // For Gemini embedding calls

// Scraper client: auto-redirect OFF so ScraperActivity follows redirects manually
// and SSRF-checks every hop (and can handle HTTPS→HTTP downgrades).
builder.Services.AddHttpClient("Scraper")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

builder.Build().Run();
