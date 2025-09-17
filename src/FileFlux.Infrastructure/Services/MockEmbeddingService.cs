#if DEBUG
using System.Security.Cryptography;
using System.Text;

namespace FileFlux.Infrastructure.Services;

/// <summary>
/// Mock implementation of IEmbeddingService for testing.
/// Uses TF-IDF-like approach for generating pseudo-embeddings.
/// Only available in DEBUG builds - excluded from production Release builds.
/// </summary>
public class MockEmbeddingService : IEmbeddingService
{
    private readonly Dictionary<string, float> _idfCache = new();
    private readonly int _dimension;
    private readonly Random _random = new();

    public MockEmbeddingService(int dimension = 384)
    {
        _dimension = dimension;
    }

    public int EmbeddingDimension => _dimension;
    public int MaxTokens => 8192;
    public bool SupportsBatchProcessing => true;

    public Task<float[]> GenerateEmbeddingAsync(
        string text,
        EmbeddingPurpose purpose = EmbeddingPurpose.Analysis,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult(new float[_dimension]);
        }

        // Generate deterministic pseudo-embedding based on text content
        var embedding = GeneratePseudoEmbedding(text, purpose);
        
        return Task.FromResult(embedding);
    }

    public async Task<IEnumerable<float[]>> GenerateBatchEmbeddingsAsync(
        IEnumerable<string> texts,
        EmbeddingPurpose purpose = EmbeddingPurpose.Analysis,
        CancellationToken cancellationToken = default)
    {
        var embeddings = new List<float[]>();
        
        foreach (var text in texts)
        {
            var embedding = await GenerateEmbeddingAsync(text, purpose, cancellationToken);
            embeddings.Add(embedding);
        }
        
        return embeddings;
    }

    public double CalculateSimilarity(float[] embedding1, float[] embedding2)
    {
        if (embedding1.Length != embedding2.Length)
        {
            throw new ArgumentException("Embeddings must have the same dimension");
        }

        // Calculate cosine similarity
        double dotProduct = 0;
        double norm1 = 0;
        double norm2 = 0;

        for (int i = 0; i < embedding1.Length; i++)
        {
            dotProduct += embedding1[i] * embedding2[i];
            norm1 += embedding1[i] * embedding1[i];
            norm2 += embedding2[i] * embedding2[i];
        }

        norm1 = Math.Sqrt(norm1);
        norm2 = Math.Sqrt(norm2);

        if (norm1 == 0 || norm2 == 0)
        {
            return 0;
        }

        return dotProduct / (norm1 * norm2);
    }

    private float[] GeneratePseudoEmbedding(string text, EmbeddingPurpose purpose)
    {
        var embedding = new float[_dimension];
        
        // Tokenize text (simple word split)
        var words = text.ToLower()
            .Split(new[] { ' ', '\n', '\r', '\t', '.', ',', '!', '?', ';', ':', '-', '(', ')', '[', ']' }, 
                   StringSplitOptions.RemoveEmptyEntries);

        if (words.Length == 0)
        {
            return embedding;
        }

        // Calculate TF-IDF-like features
        var wordFreq = new Dictionary<string, float>();
        foreach (var word in words)
        {
            if (wordFreq.ContainsKey(word))
                wordFreq[word]++;
            else
                wordFreq[word] = 1;
        }

        // Generate feature vector based on word frequencies and hash
        int index = 0;
        foreach (var kvp in wordFreq)
        {
            var word = kvp.Key;
            var freq = kvp.Value / words.Length; // TF
            
            // Simple IDF simulation
            if (!_idfCache.ContainsKey(word))
            {
                _idfCache[word] = (float)Math.Log(1000.0 / (1 + _random.Next(1, 100)));
            }
            var idf = _idfCache[word];
            
            // Hash word to get consistent indices
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(word));
                for (int i = 0; i < Math.Min(4, hash.Length); i++)
                {
                    var idx = (hash[i] * 7 + i * 13) % _dimension;
                    embedding[idx] += freq * idf * (1 + (float)purpose * 0.1f);
                }
            }
        }

        // Add some semantic features based on text characteristics
        AddSemanticFeatures(embedding, text, words);

        // Normalize the embedding
        NormalizeEmbedding(embedding);

        return embedding;
    }

    private void AddSemanticFeatures(float[] embedding, string text, string[] words)
    {
        // Add features for text characteristics
        var features = new Dictionary<int, float>
        {
            // Length features
            [0] = text.Length / 1000f,
            [1] = words.Length / 100f,
            
            // Punctuation features
            [2] = CountOccurrences(text, '.') / 10f,
            [3] = CountOccurrences(text, '?') / 5f,
            [4] = CountOccurrences(text, '!') / 5f,
            
            // Structure features
            [5] = CountOccurrences(text, '\n') / 10f,
            [6] = CountOccurrences(text, '#') / 5f,  // Markdown headers
            [7] = text.Contains("```") ? 1f : 0f,    // Code blocks
            [8] = text.Contains("TABLE") ? 1f : 0f,  // Tables
            [9] = text.Contains("LIST") ? 1f : 0f,   // Lists
            
            // Content type indicators
            [10] = ContainsPattern(text, @"\d+\.\d+") ? 1f : 0f,  // Numbers
            [11] = ContainsPattern(text, @"https?://") ? 1f : 0f, // URLs
            [12] = ContainsPattern(text, @"\w+@\w+") ? 1f : 0f,    // Emails
        };

        foreach (var kvp in features)
        {
            if (kvp.Key < embedding.Length)
            {
                embedding[kvp.Key] = Math.Min(1f, embedding[kvp.Key] + kvp.Value);
            }
        }

        // Add semantic similarity features for common topics
        AddTopicFeatures(embedding, words);
    }

    private void AddTopicFeatures(float[] embedding, string[] words)
    {
        var topics = new Dictionary<string, int>
        {
            ["technical"] = 20,
            ["legal"] = 30,
            ["medical"] = 40,
            ["financial"] = 50,
            ["academic"] = 60,
            ["ml"] = 70,  // Machine learning specific
            ["weather"] = 80,  // Weather specific
        };

        var topicKeywords = new Dictionary<string, string[]>
        {
            ["technical"] = new[] { "code", "function", "api", "system", "data", "algorithm", "software", "```", "python", "def", "print" },
            ["legal"] = new[] { "law", "legal", "contract", "agreement", "clause", "liability", "court" },
            ["medical"] = new[] { "patient", "treatment", "diagnosis", "medical", "health", "disease", "symptom" },
            ["financial"] = new[] { "finance", "money", "investment", "market", "profit", "revenue", "cost" },
            ["academic"] = new[] { "research", "study", "analysis", "theory", "hypothesis", "conclusion", "abstract" },
            ["ml"] = new[] { "machine", "learning", "algorithm", "data", "model", "training", "artificial", "intelligence", "prediction", "analyze", "patterns", "trends" },
            ["weather"] = new[] { "weather", "sunny", "warm", "outdoor", "temperature", "degrees", "climate", "rain", "snow" },
        };

        foreach (var topic in topics)
        {
            var keywords = topicKeywords[topic.Key];
            var score = 0f;
            
            // Count keyword matches with boost for exact matches
            foreach (var keyword in keywords)
            {
                var count = words.Count(w => w.ToLower().Contains(keyword.ToLower()));
                score += count > 0 ? (float)count / words.Length : 0;
            }
            
            // Boost score for stronger topic relevance
            score = Math.Min(1f, score * 2);
            
            if (topic.Value < embedding.Length)
            {
                embedding[topic.Value] += score;
            }
        }
    }

    private void NormalizeEmbedding(float[] embedding)
    {
        float norm = 0;
        for (int i = 0; i < embedding.Length; i++)
        {
            norm += embedding[i] * embedding[i];
        }
        
        norm = (float)Math.Sqrt(norm);
        
        if (norm > 0)
        {
            for (int i = 0; i < embedding.Length; i++)
            {
                embedding[i] /= norm;
            }
        }
    }

    private int CountOccurrences(string text, char c)
    {
        return text.Count(ch => ch == c);
    }

    private bool ContainsPattern(string text, string pattern)
    {
        return System.Text.RegularExpressions.Regex.IsMatch(text, pattern);
    }
}
#endif