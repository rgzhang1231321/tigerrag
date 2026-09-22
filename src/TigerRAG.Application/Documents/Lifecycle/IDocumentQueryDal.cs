namespace TigerRAG.Application.Documents.Lifecycle;

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