using Microsoft.EntityFrameworkCore;
using TigerRAG.Application.Documents.Lifecycle;
using TigerRAG.Domain.Documents;
using TigerRAG.Infrastructure.Persistence;

namespace TigerRAG.Infrastructure.Documents.Dal;

/// <summary>文档查询 DAL 的 EF 实现：只读路径，返回 Application DTO。</summary>
public sealed class DocumentQueryDal(TigerRagDbContext dbContext) : IDocumentQueryDal
{
    public async Task<DocumentSummary?> FindSummaryAsync(Guid id, CancellationToken cancellationToken)
    {
        var record = await dbContext.Documents
            .AsNoTracking()
            .FirstOrDefaultAsync(document => document.Id == id, cancellationToken);
        return record is null ? null : ToSummary(record);
    }

    public async Task<IReadOnlyList<DocumentSummary>> ListByKbAsync(Guid kbId, CancellationToken cancellationToken)
    {
        var records = await dbContext.Documents
            .AsNoTracking()
            .Where(document => document.KnowledgeBaseId == kbId)
            .OrderByDescending(document => document.CreatedAt)
            .ToListAsync(cancellationToken);
        return records.Select(ToSummary).ToList();
    }

    public async Task<IReadOnlyList<Guid>> ListNonProcessingIdsByKbAsync(Guid kbId, CancellationToken cancellationToken)
    {
        return await dbContext.Documents
            .AsNoTracking()
            .Where(document => document.KnowledgeBaseId == kbId && document.Status != DocumentStatus.Processing)
            .Select(document => document.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ListPendingIdsAsync(CancellationToken cancellationToken)
    {
        return await dbContext.Documents
            .AsNoTracking()
            .Where(document => document.Status == DocumentStatus.Pending)
            .Select(document => document.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ListTimedOutProcessingIdsAsync(DateTimeOffset olderThan, CancellationToken cancellationToken)
    {
        return await dbContext.Documents
            .AsNoTracking()
            .Where(document => document.Status == DocumentStatus.Processing && document.UpdatedAt < olderThan)
            .Select(document => document.Id)
            .ToListAsync(cancellationToken);
    }

    /// <summary>把 EF 实体行映射为 Application 摘要；字段名从数据库小写映射到 PascalCase 属性。</summary>
    private static DocumentSummary ToSummary(Infrastructure.Persistence.Entities.Documents.document_record record) => new(
        record.Id,
        record.KnowledgeBaseId,
        record.FileName,
        record.MimeType,
        record.StoragePath,
        record.Size,
        record.Status,
        record.ChunkCount,
        record.FailureReason,
        record.CreatedBy,
        record.CreatedAt,
        record.UpdatedAt);
}