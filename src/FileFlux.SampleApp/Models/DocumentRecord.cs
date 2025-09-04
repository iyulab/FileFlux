using System.ComponentModel.DataAnnotations;

namespace FileFlux.SampleApp.Models;

/// <summary>
/// 문서 레코드 - SQLite 저장용
/// </summary>
public class DocumentRecord
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// 원본 파일 경로
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// 파일 해시값 (중복 처리 방지)
    /// </summary>
    public string FileHash { get; set; } = string.Empty;

    /// <summary>
    /// 파일 크기 (bytes)
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// 처리 날짜
    /// </summary>
    public DateTime ProcessedAt { get; set; }

    /// <summary>
    /// 사용된 청킹 전략
    /// </summary>
    public string ChunkingStrategy { get; set; } = string.Empty;

    /// <summary>
    /// 생성된 청크 수
    /// </summary>
    public int ChunkCount { get; set; }

    /// <summary>
    /// 관련된 청크들
    /// </summary>
    public virtual ICollection<ChunkRecord> Chunks { get; set; } = new List<ChunkRecord>();
}

/// <summary>
/// 청크 레코드 - SQLite 저장용
/// </summary>
public class ChunkRecord
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// 문서 ID (외래키)
    /// </summary>
    public int DocumentId { get; set; }

    /// <summary>
    /// 청크 순서
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// 청크 내용
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 토큰 수 추정값
    /// </summary>
    public int EstimatedTokens { get; set; }

    /// <summary>
    /// 청크 메타데이터 (JSON)
    /// </summary>
    public string? Metadata { get; set; }

    /// <summary>
    /// OpenAI 임베딩 벡터 (JSON 배열)
    /// </summary>
    public string? EmbeddingVector { get; set; }

    /// <summary>
    /// 임베딩 생성 날짜
    /// </summary>
    public DateTime? EmbeddingCreatedAt { get; set; }

    /// <summary>
    /// 관련된 문서
    /// </summary>
    public virtual DocumentRecord? Document { get; set; }
}

/// <summary>
/// RAG 쿼리 기록
/// </summary>
public class QueryRecord
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// 사용자 쿼리
    /// </summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// 쿼리 임베딩 벡터 (JSON 배열)
    /// </summary>
    public string? QueryEmbedding { get; set; }

    /// <summary>
    /// 검색된 관련 청크 ID들 (JSON 배열)
    /// </summary>
    public string? RelevantChunkIds { get; set; }

    /// <summary>
    /// OpenAI 응답
    /// </summary>
    public string? Response { get; set; }

    /// <summary>
    /// 쿼리 실행 시간
    /// </summary>
    public DateTime QueryTime { get; set; }

    /// <summary>
    /// 응답 생성에 걸린 시간 (ms)
    /// </summary>
    public int ResponseTimeMs { get; set; }
}