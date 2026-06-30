namespace DistributedRag.Shared.Configuration;

/// <summary>
/// Root configuration for the Distributed RAG Platform.
/// Bound from appsettings.json / environment variables.
/// </summary>
public class MongoDbSettings
{
    public const string SectionName = "MongoDB";

    /// <summary>
    /// MongoDB Atlas connection string. Supplied at runtime via environment
    /// variables or Azure app settings (key 'MongoDB:ConnectionString').
    /// Never commit a real connection string to source control.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Database name. Default: distributed_rag
    /// </summary>
    public string DatabaseName { get; set; } = "distributed_rag";
}

public class ServiceBusSettings
{
    public const string SectionName = "ServiceBus";

    /// <summary>
    /// Azure Service Bus connection string.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Queue name for URL processing messages.
    /// </summary>
    public string QueueName { get; set; } = "process-url-queue";
}

public class GroqSettings
{
    public const string SectionName = "Groq";

    /// <summary>
    /// Groq API key.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// LLM model to use for RAG queries.
    /// </summary>
    public string Model { get; set; } = "llama-3.3-70b-versatile";

    /// <summary>
    /// Maximum tokens in LLM response.
    /// </summary>
    public int MaxTokens { get; set; } = 1024;

    /// <summary>
    /// Temperature for response generation (0.0 = deterministic, 1.0 = creative).
    /// </summary>
    public double Temperature { get; set; } = 0.1;
}

public class GeminiSettings
{
    public const string SectionName = "Gemini";

    /// <summary>
    /// Google Gemini API key (free tier — from Google AI Studio).
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Embedding model. text-embedding-004 produces 768-dim vectors.
    /// </summary>
    public string EmbeddingModel { get; set; } = "text-embedding-004";

    /// <summary>
    /// Number of embedding dimensions (must match the MongoDB Atlas vector index).
    /// </summary>
    public int EmbeddingDimensions { get; set; } = 768;
}
