namespace TigerRAG.Application.Documents.Lifecycle;

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