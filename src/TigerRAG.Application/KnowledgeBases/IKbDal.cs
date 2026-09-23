using TigerRAG.Domain.KnowledgeBases;

namespace TigerRAG.Application.KnowledgeBases;

/// <summary>知识库 DAL 端口：仅持久化形态，不暴露 EF 实体。</summary>
public interface IKbDal
{
    /// <summary>按 Id 查找；找不到返回 null。</summary>
    Task<KnowledgeBase?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>列出知识库：Admin 返回全部，非 Admin 仅返回 OwnerId == actorId 的 KB。不计入 DocumentCount / OwnerName 时返回默认值。</summary>
    Task<IReadOnlyList<KnowledgeBaseSummary>> ListAsync(Guid actorId, bool isAdmin, CancellationToken cancellationToken);

    /// <summary>插入新知识库；调用方负责事务与审计。</summary>
    Task InsertAsync(KnowledgeBase knowledgeBase, CancellationToken cancellationToken);

    /// <summary>更新已存在的知识库；调用方负责事务与审计。</summary>
    Task UpdateAsync(KnowledgeBase knowledgeBase, CancellationToken cancellationToken);

    /// <summary>列出指定知识库下所有文档 Id（含已删除以外的任意状态），用于级联删除。</summary>
    Task<IReadOnlyList<Guid>> ListDocumentIdsAsync(Guid kbId, CancellationToken cancellationToken);

    /// <summary>删除知识库行；调用方须先级联删完 documents/chunks/permissions。</summary>
    Task DeleteAsync(Guid kbId, CancellationToken cancellationToken);

    /// <summary>列出知识库下所有非 Processing 文档的 Id（重索引时使用）。</summary>
    Task<IReadOnlyList<Guid>> ListNonProcessingDocumentIdsAsync(Guid kbId, CancellationToken cancellationToken);

    /// <summary>批量重置知识库下非 Processing 文档为 Pending（重索引时使用）。</summary>
    Task<int> ResetNonProcessingToPendingAsync(Guid kbId, DateTimeOffset updatedAt, CancellationToken cancellationToken);
}
