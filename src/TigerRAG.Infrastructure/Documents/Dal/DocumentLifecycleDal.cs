using Microsoft.EntityFrameworkCore;
using TigerRAG.Application.Documents.Lifecycle;
using TigerRAG.Domain.Documents;
using TigerRAG.Infrastructure.Persistence;
using TigerRAG.Infrastructure.Persistence.Entities.Documents;

namespace TigerRAG.Infrastructure.Documents.Dal;

/// <summary>文档生命周期 DAL 的 EF 实现：写路径，调用方负责事务。</summary>
public sealed class DocumentLifecycleDal(TigerRagDbContext dbContext) : IDocumentLifecycleDal
{
    public async Task InsertAsync(DocumentSummary document, CancellationToken cancellationToken)
    {
        await dbContext.Documents.AddAsync(ToRecord(document), cancellationToken);
    }

    public Task DeleteChunksAsync(Guid documentId, CancellationToken cancellationToken)
    {
        return dbContext.DocumentChunks
            .Where(chunk => chunk.DocumentId == documentId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public Task DeletePermissionsAsync(Guid documentId, CancellationToken cancellationToken)
    {
        return dbContext.DocumentPermissions
            .Where(permission => permission.DocumentId == documentId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public Task DeleteAsync(Guid documentId, CancellationToken cancellationToken)
    {
        return dbContext.Documents
            .Where(document => document.Id == documentId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>原子认领：UPDATE document_record SET Status=Processing WHERE Id=@id AND Status=Pending。</summary>
    public async Task<bool> TryClaimAsync(Guid documentId, DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        var rowsAffected = await dbContext.Documents
            .Where(document => document.Id == documentId && document.Status == DocumentStatus.Pending)
            .ExecuteUpdateAsync(update => update
                .SetProperty(document => document.Status, DocumentStatus.Processing)
                .SetProperty(document => document.UpdatedAt, updatedAt),
                cancellationToken);
        return rowsAffected == 1;
    }

    /// <summary>将 Processing 文档重置为 Pending（Worker 超时恢复用）；非 Processing 时影响 0 行。</summary>
    public async Task<bool> ResetProcessingToPendingAsync(Guid documentId, DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        var rowsAffected = await dbContext.Documents
            .Where(document => document.Id == documentId && document.Status == DocumentStatus.Processing)
            .ExecuteUpdateAsync(update => update
                .SetProperty(document => document.Status, DocumentStatus.Pending)
                .SetProperty(document => document.FailureReason, (string?)null)
                .SetProperty(document => document.UpdatedAt, updatedAt),
                cancellationToken);
        return rowsAffected == 1;
    }

    /// <summary>单文档重索引：Status=Pending / ChunkCount=0 / FailureReason=NULL（重索引用）。</summary>
    public async Task<bool> MarkReindexAsync(Guid documentId, DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        var rowsAffected = await dbContext.Documents
            .Where(document => document.Id == documentId && document.Status != DocumentStatus.Processing)
            .ExecuteUpdateAsync(update => update
                .SetProperty(document => document.Status, DocumentStatus.Pending)
                .SetProperty(document => document.ChunkCount, 0)
                .SetProperty(document => document.FailureReason, (string?)null)
                .SetProperty(document => document.UpdatedAt, updatedAt),
                cancellationToken);
        return rowsAffected == 1;
    }

    /// <summary>把 Application 摘要映射为 EF 实体行；字段名全部小写对齐数据库。</summary>
    private static document_record ToRecord(DocumentSummary summary) => new()
    {
        Id = summary.Id,
        KnowledgeBaseId = summary.KbId,
        FileName = summary.FileName,
        MimeType = summary.MimeType,
        StoragePath = summary.StoragePath,
        Size = summary.Size,
        Status = summary.Status,
        ChunkCount = summary.ChunkCount,
        FailureReason = summary.FailureReason,
        CreatedBy = summary.CreatedBy,
        CreatedAt = summary.CreatedAt,
        UpdatedAt = summary.UpdatedAt,
    };
}