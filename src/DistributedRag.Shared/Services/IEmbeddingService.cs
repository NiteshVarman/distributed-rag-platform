namespace DistributedRag.Shared.Services;

/// <summary>
/// Abstraction over embedding providers (currently Gemini) so the embedding
/// backend can be swapped without touching the ingestion or query pipeline.
/// </summary>
public interface IEmbeddingService
{
    /// <summary>Generates an embedding vector for a single text.</summary>
    Task<float[]> GenerateEmbeddingAsync(string text);

    /// <summary>Generates embedding vectors for a batch of texts (one per input).</summary>
    Task<List<float[]>> GenerateEmbeddingsAsync(List<string> texts);
}
