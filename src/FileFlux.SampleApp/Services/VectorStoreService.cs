using FileFlux.Domain;
using FileFlux.SampleApp.Data;
using FileFlux.SampleApp.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenAI.Embeddings;
using OpenAI.Chat;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Diagnostics;

namespace FileFlux.SampleApp.Services;

/// <summary>
/// SQLite 기반 벡터 스토어 서비스 구현
/// </summary>
public class VectorStoreService : IVectorStoreService
{
    private readonly FileFluxDbContext _context;
    private readonly EmbeddingClient _embeddingClient;
    private readonly ChatClient _chatClient;
    private readonly ILogger<VectorStoreService> _logger;

    // OpenAI 모델 설정
    private const string EmbeddingModel = "text-embedding-3-small";
    private const string ChatModel = "gpt-4o-mini";

    public VectorStoreService(
        FileFluxDbContext context,
        EmbeddingClient embeddingClient,
        ChatClient chatClient,
        ILogger<VectorStoreService> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _embeddingClient = embeddingClient ?? throw new ArgumentNullException(nameof(embeddingClient));
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<DocumentRecord> StoreDocumentAsync(
        string filePath,
        IEnumerable<DocumentChunk> chunks,
        string strategy,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Storing document: {FilePath} with {ChunkCount} chunks", filePath, chunks.Count());

        var fileInfo = new FileInfo(filePath);
        var fileHash = await ComputeFileHashAsync(filePath, cancellationToken);

        // 중복 문서 확인
        var existingDoc = await _context.Documents
            .FirstOrDefaultAsync(d => d.FileHash == fileHash, cancellationToken);

        if (existingDoc != null)
        {
            _logger.LogInformation("Document already exists with hash: {FileHash}", fileHash);
            return existingDoc;
        }

        // 새 문서 레코드 생성
        var document = new DocumentRecord
        {
            FilePath = filePath,
            FileHash = fileHash,
            FileSize = fileInfo.Length,
            ProcessedAt = DateTime.UtcNow,
            ChunkingStrategy = strategy,
            ChunkCount = chunks.Count()
        };

        _context.Documents.Add(document);
        await _context.SaveChangesAsync(cancellationToken);

        // 청크들을 배치로 처리하여 임베딩 생성
        var chunkList = chunks.ToList();
        var chunkRecords = new List<ChunkRecord>();

        const int batchSize = 100; // OpenAI API 제한 고려
        for (int i = 0; i < chunkList.Count; i += batchSize)
        {
            var batch = chunkList.Skip(i).Take(batchSize).ToList();
            var batchRecords = await ProcessChunkBatchAsync(document.Id, batch, i, cancellationToken);
            chunkRecords.AddRange(batchRecords);
        }

        _context.Chunks.AddRange(chunkRecords);
        await _context.SaveChangesAsync(cancellationToken);

        document.Chunks = chunkRecords;

        _logger.LogInformation("Successfully stored document with {ChunkCount} chunks", chunkRecords.Count);
        return document;
    }

    public async Task<IEnumerable<ChunkRecord>> SearchSimilarChunksAsync(
        string query,
        int topK = 5,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Searching for similar chunks: {Query}", query);

        // 쿼리 임베딩 생성
        var queryEmbedding = await GenerateEmbeddingAsync(query, cancellationToken);

        // 모든 청크 조회 (실제 환경에서는 더 효율적인 벡터 검색 필요)
        var allChunks = await _context.Chunks
            .Include(c => c.Document)
            .Where(c => c.EmbeddingVector != null)
            .ToListAsync(cancellationToken);

        // 코사인 유사도 계산하여 정렬
        var similarities = allChunks
            .Select(chunk => new
            {
                Chunk = chunk,
                Similarity = CalculateCosineSimilarity(
                    queryEmbedding,
                    JsonSerializer.Deserialize<float[]>(chunk.EmbeddingVector!)!)
            })
            .OrderByDescending(x => x.Similarity)
            .Take(topK)
            .Select(x => x.Chunk);

        _logger.LogInformation("Found {Count} similar chunks", similarities.Count());
        return similarities;
    }

    public async Task<QueryRecord> ExecuteRagQueryAsync(
        string query,
        int topK = 5,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("Executing RAG query: {Query}", query);

        // 관련 청크 검색
        var relevantChunks = await SearchSimilarChunksAsync(query, topK, cancellationToken);
        var chunkIds = relevantChunks.Select(c => c.Id).ToArray();

        // 컨텍스트 구성
        var context = string.Join("\n\n", relevantChunks.Select((chunk, index) =>
            $"[Context {index + 1}]\nSource: {chunk.Document?.FilePath ?? "Unknown"}\nContent: {chunk.Content}"));

        // RAG 프롬프트 구성
        var ragPrompt = $"""
            다음 컨텍스트를 바탕으로 사용자의 질문에 답변해주세요.
            
            === 컨텍스트 ===
            {context}
            
            === 사용자 질문 ===
            {query}
            
            === 답변 지침 ===
            - 제공된 컨텍스트를 기반으로 정확하고 유용한 답변을 제공하세요
            - 컨텍스트에 없는 정보는 추측하지 말고 "제공된 정보로는 확인할 수 없습니다"라고 명시하세요
            - 가능하면 어느 소스에서 정보를 얻었는지 언급하세요
            """;

        // OpenAI 채팅 완성 요청
        var chatCompletion = await _chatClient.CompleteChatAsync(
            [
                new SystemChatMessage("당신은 문서를 기반으로 정확한 정보를 제공하는 도움이 되는 AI 어시스턴트입니다."),
                new UserChatMessage(ragPrompt)
            ],
            cancellationToken: cancellationToken);

        stopwatch.Stop();

        // 쿼리 기록 저장
        var queryRecord = new QueryRecord
        {
            Query = query,
            QueryEmbedding = JsonSerializer.Serialize(await GenerateEmbeddingAsync(query, cancellationToken)),
            RelevantChunkIds = JsonSerializer.Serialize(chunkIds),
            Response = chatCompletion.Value.Content[0].Text,
            QueryTime = DateTime.UtcNow,
            ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
        };

        _context.Queries.Add(queryRecord);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("RAG query completed in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
        return queryRecord;
    }

    public async Task<IEnumerable<DocumentRecord>> GetDocumentsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Documents
            .Include(d => d.Chunks)
            .OrderByDescending(d => d.ProcessedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<QueryRecord>> GetQueryHistoryAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        return await _context.Queries
            .OrderByDescending(q => q.QueryTime)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    private async Task<List<ChunkRecord>> ProcessChunkBatchAsync(
        int documentId,
        List<DocumentChunk> chunks,
        int startIndex,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Processing chunk batch: {StartIndex}-{EndIndex}", startIndex, startIndex + chunks.Count - 1);

        // 배치로 임베딩 생성
        var contents = chunks.Select(c => c.Content).ToList();
        var embeddings = await GenerateBatchEmbeddingsAsync(contents, cancellationToken);

        var chunkRecords = new List<ChunkRecord>();
        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            var embedding = embeddings[i];

            var chunkRecord = new ChunkRecord
            {
                DocumentId = documentId,
                Order = startIndex + i,
                Content = chunk.Content,
                EstimatedTokens = chunk.EstimatedTokens,
                Metadata = JsonSerializer.Serialize(chunk.Metadata),
                EmbeddingVector = JsonSerializer.Serialize(embedding),
                EmbeddingCreatedAt = DateTime.UtcNow
            };

            chunkRecords.Add(chunkRecord);
        }

        return chunkRecords;
    }

    private async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken)
    {
        var embedding = await _embeddingClient.GenerateEmbeddingAsync(text, cancellationToken: cancellationToken);
        return embedding.Value.ToFloats().ToArray();
    }

    private async Task<List<float[]>> GenerateBatchEmbeddingsAsync(List<string> texts, CancellationToken cancellationToken)
    {
        var embeddings = await _embeddingClient.GenerateEmbeddingsAsync(texts, cancellationToken: cancellationToken);
        return embeddings.Value.Select(e => e.ToFloats().ToArray()).ToList();
    }

    private static async Task<string> ComputeFileHashAsync(string filePath, CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(filePath);
        using var sha256 = SHA256.Create();
        var hashBytes = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static float CalculateCosineSimilarity(float[] vector1, float[] vector2)
    {
        if (vector1.Length != vector2.Length)
            throw new ArgumentException("Vectors must have the same length");

        float dotProduct = 0;
        float magnitude1 = 0;
        float magnitude2 = 0;

        for (int i = 0; i < vector1.Length; i++)
        {
            dotProduct += vector1[i] * vector2[i];
            magnitude1 += vector1[i] * vector1[i];
            magnitude2 += vector2[i] * vector2[i];
        }

        magnitude1 = MathF.Sqrt(magnitude1);
        magnitude2 = MathF.Sqrt(magnitude2);

        if (magnitude1 == 0 || magnitude2 == 0)
            return 0;

        return dotProduct / (magnitude1 * magnitude2);
    }
}