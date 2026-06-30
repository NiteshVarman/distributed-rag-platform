using DistributedRag.Api.Endpoints;
using DistributedRag.Api.Services;
using DistributedRag.Shared.Configuration;
using DistributedRag.Shared.Services;

var builder = WebApplication.CreateBuilder(args);

// ─────────────────────────────────────────────
// Configuration Binding
// ─────────────────────────────────────────────
builder.Services.Configure<MongoDbSettings>(
    builder.Configuration.GetSection(MongoDbSettings.SectionName));
builder.Services.Configure<ServiceBusSettings>(
    builder.Configuration.GetSection(ServiceBusSettings.SectionName));
builder.Services.Configure<GeminiSettings>(
    builder.Configuration.GetSection(GeminiSettings.SectionName));
builder.Services.Configure<GroqSettings>(
    builder.Configuration.GetSection(GroqSettings.SectionName));

// ─────────────────────────────────────────────
// Services — Singleton (thread-safe, connection-pooled)
// ─────────────────────────────────────────────
builder.Services.AddSingleton<MongoDbService>();
builder.Services.AddSingleton<ServiceBusPublisher>();
builder.Services.AddSingleton<IEmbeddingService, GeminiEmbeddingService>();
builder.Services.AddSingleton<GroqLlmService>();
builder.Services.AddSingleton<RagQueryService>();
builder.Services.AddHttpClient(); // For Gemini + Groq HTTP calls

// CORS — Allow Chrome Extension and local development
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowChromeExtension", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// OpenAPI / Swagger
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

// ─────────────────────────────────────────────
// Middleware Pipeline
// ─────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    // Swagger UI not included by default in basic .NET 8 web templates
}

app.UseCors("AllowChromeExtension");
app.UseHttpsRedirection();

// ─────────────────────────────────────────────
// Health Check Endpoint
// ─────────────────────────────────────────────
app.MapGet("/api/health", async (MongoDbService mongoDb) =>
{
    var isHealthy = await mongoDb.HealthCheckAsync();
    return isHealthy
        ? Results.Ok(new { status = "healthy", database = "connected", timestamp = DateTime.UtcNow })
        : Results.Json(
            new { status = "unhealthy", database = "disconnected", timestamp = DateTime.UtcNow },
            statusCode: 503);
})
.WithName("HealthCheck")
.WithTags("System");

// ─────────────────────────────────────────────
// Map API Endpoints
// ─────────────────────────────────────────────
ProcessUrlEndpoint.Map(app);
JobStatusEndpoint.Map(app);
QueryEndpoint.Map(app);
TextActionEndpoint.Map(app);
PageStatusEndpoint.Map(app);

// ─────────────────────────────────────────────
// Graceful Shutdown — Dispose ServiceBusPublisher
// ─────────────────────────────────────────────
app.Lifetime.ApplicationStopping.Register(() =>
{
    var publisher = app.Services.GetService<ServiceBusPublisher>();
    publisher?.DisposeAsync().AsTask().GetAwaiter().GetResult();
});

app.Run();
