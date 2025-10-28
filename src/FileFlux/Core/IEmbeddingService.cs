namespace FileFlux;

/// <summary>
/// Service for generating embeddings for document analysis purposes.
/// This is NOT for final storage embeddings, but for semantic analysis during chunking.
/// </summary>
public interface IEmbeddingService
{
    /// <summary>
    /// Generates an embedding vector for the given text.
    /// </summary>
    /// <param name="text">The text to generate embedding for</param>
    /// <param name="purpose">The purpose of the embedding (default: Analysis)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The embedding vector as float array</returns>
    Task<float[]> GenerateEmbeddingAsync(
        string text,
        EmbeddingPurpose purpose = EmbeddingPurpose.Analysis,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates embeddings for multiple texts in batch.
    /// </summary>
    /// <param name="texts">The texts to generate embeddings for</param>
    /// <param name="purpose">The purpose of the embeddings</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of embedding vectors</returns>
    Task<IEnumerable<float[]>> GenerateBatchEmbeddingsAsync(
        IEnumerable<string> texts,
        EmbeddingPurpose purpose = EmbeddingPurpose.Analysis,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculates the cosine similarity between two embedding vectors.
    /// </summary>
    /// <param name="embedding1">First embedding vector</param>
    /// <param name="embedding2">Second embedding vector</param>
    /// <returns>Similarity score between 0 and 1</returns>
    double CalculateSimilarity(float[] embedding1, float[] embedding2);

    /// <summary>
    /// Gets the dimension size of the embedding vectors.
    /// </summary>
    int EmbeddingDimension { get; }

    /// <summary>
    /// Gets the maximum number of tokens that can be processed.
    /// </summary>
    int MaxTokens { get; }

    /// <summary>
    /// Indicates whether batch processing is supported.
    /// </summary>
    bool SupportsBatchProcessing { get; }
}

/// <summary>
/// Specifies the purpose of embedding generation to optimize model selection.
/// </summary>
public enum EmbeddingPurpose
{
    /// <summary>
    /// For document analysis during chunking (lightweight, fast model)
    /// </summary>
    Analysis,

    /// <summary>
    /// For semantic search operations (medium quality)
    /// </summary>
    SemanticSearch,

    /// <summary>
    /// For final storage in vector database (high quality, consumer app responsibility)
    /// </summary>
    Storage
}

/// <summary>
/// Configuration options for embedding generation.
/// </summary>
public class EmbeddingOptions
{
    /// <summary>
    /// The model to use for embedding generation.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Whether to normalize the embeddings.
    /// </summary>
    public bool Normalize { get; set; } = true;

    /// <summary>
    /// The pooling strategy for token embeddings.
    /// </summary>
    public PoolingStrategy Pooling { get; set; } = PoolingStrategy.Mean;

    /// <summary>
    /// Custom dimensions for embedding (if supported by model).
    /// </summary>
    public int? Dimensions { get; set; }
}

/// <summary>
/// Pooling strategy for combining token embeddings.
/// </summary>
public enum PoolingStrategy
{
    /// <summary>
    /// Mean pooling across all tokens
    /// </summary>
    Mean,

    /// <summary>
    /// Max pooling across all tokens
    /// </summary>
    Max,

    /// <summary>
    /// Use CLS token embedding (for BERT-like models)
    /// </summary>
    Cls,

    /// <summary>
    /// Weighted mean based on token importance
    /// </summary>
    WeightedMean
}
