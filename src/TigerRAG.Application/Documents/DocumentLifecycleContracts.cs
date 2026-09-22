using TigerRAG.Domain.Documents;

namespace TigerRAG.Application.Documents;

/// <summary>文档摘要 DTO；Application 层专用，不暴露 EF 实体。</summary>
public sealed record DocumentSummary(
    Guid Id,
    Guid KbId,
    string FileName,
    string? MimeType,
    string StoragePath,
    DocumentStatus Status,
    int ChunkCount,
    string? FailureReason,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>文档查询 DAL：只读路径，返回 Application DTO。</summary>
public interface IDocumentQueryDal
{
    /// <summary>按 Id 查找摘要；找不到返回 null。</summary>
    Task<DocumentSummary?> FindSummaryAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>列出指定 KB 下所有文档摘要（不过滤 ACL；ACL 在 Service 层叠加）。</summary>
    Task<IReadOnlyList<DocumentSummary>> ListByKbAsync(Guid kbId, CancellationToken cancellationToken);

    /// <summary>列出指定 KB 下状态非 Processing 的文档 Id（重索引时使用）。</summary>
    Task<IReadOnlyList<Guid>> ListNonProcessingIdsByKbAsync(Guid kbId, CancellationToken cancellationToken);

    /// <summary>列出所有 Pending 文档 Id（Worker 启动恢复时使用）。</summary>
    Task<IReadOnlyList<Guid>> ListPendingIdsAsync(CancellationToken cancellationToken);

    /// <summary>列出所有 UpdatedAt 早于阈值且状态为 Processing 的文档 Id（Worker 超时恢复时使用）。</summary>
    Task<IReadOnlyList<Guid>> ListTimedOutProcessingIdsAsync(DateTimeOffset olderThan, CancellationToken cancellationToken);
}

/// <summary>文档生命周期 DAL：写路径，供 Service 在事务内调用。</summary>
public interface IDocumentLifecycleDal
{
    /// <summary>插入新文档（Pending 状态）；调用方负责事务与审计。</summary>
    Task InsertAsync(DocumentSummary document, CancellationToken cancellationToken);

    /// <summary>删除文档的所有分块（document_chunk_record）。</summary>
    Task DeleteChunksAsync(Guid documentId, CancellationToken cancellationToken);

    /// <summary>删除文档的所有权限（document_permission_record）。</summary>
    Task DeletePermissionsAsync(Guid documentId, CancellationToken cancellationToken);

    /// <summary>删除文档行（document_record）。</summary>
    Task DeleteAsync(Guid documentId, CancellationToken cancellationToken);

    /// <summary>原子认领：UPDATE document_record SET Status=Processing WHERE Id=@id AND Status=Pending。影响行数=1 表示认领成功。</summary>
    Task<bool> TryClaimAsync(Guid documentId, DateTimeOffset updatedAt, CancellationToken cancellationToken);

    /// <summary>将 Processing 文档重置为 Pending（Worker 超时恢复用）。</summary>
    Task<bool> ResetProcessingToPendingAsync(Guid documentId, DateTimeOffset updatedAt, CancellationToken cancellationToken);

    /// <summary>将单文档重置为 Pending（重索引用）。</summary>
    Task<bool> MarkReindexAsync(Guid documentId, DateTimeOffset updatedAt, CancellationToken cancellationToken);
}
