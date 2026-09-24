using TigerRAG.Domain.Documents;

namespace TigerRAG.Application.Documents.Lifecycle;

/// <summary>文档摘要 DTO；Application 层专用，不暴露 EF 实体。</summary>
public sealed record DocumentSummary(
    Guid Id,
    Guid KbId,
    string FileName,
    string? MimeType,
    string StoragePath,
    long Size,
    DocumentStatus Status,
    int ChunkCount,
    string? FailureReason,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);