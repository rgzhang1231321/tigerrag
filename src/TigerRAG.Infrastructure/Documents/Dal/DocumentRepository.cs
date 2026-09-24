using Microsoft.EntityFrameworkCore;
using TigerRAG.Application.Documents.Indexing.Interface;
using TigerRAG.Domain.Documents;
using TigerRAG.Infrastructure.Persistence;

namespace TigerRAG.Infrastructure.Documents.Dal;

/// <summary>IDocumentRepository 的 EF 实现：domain Document ↔ document_record 映射。</summary>
public sealed class DocumentRepository(TigerRagDbContext dbContext) : IDocumentRepository
{
    public async Task<Document?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        var record = await dbContext.Documents
            .AsNoTracking()
            .FirstOrDefaultAsync(document => document.Id == id, cancellationToken);
        return record is null ? null : ToDomain(record);
    }

    public async Task SaveAsync(Document document, CancellationToken cancellationToken)
    {
        // 文档状态由 Domain 方法保证合法性；此处把可变字段写回跟踪实体，由事务内的 SaveChanges 统一落库。
        var entry = await dbContext.Documents
            .FirstOrDefaultAsync(d => d.Id == document.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Document {document.Id} was not found.");

        entry.Status = document.Status;
        entry.ChunkCount = document.ChunkCount;
        entry.FailureReason = document.FailureReason;
        // UpdatedAt 由 EF 自动维护；此处不覆盖以保证事务时序一致。
    }

    /// <summary>把 EF 实体行映射为领域对象；跳过业务不变量校验（数据已过校验入库）。</summary>
    private static Document ToDomain(Infrastructure.Persistence.Entities.Documents.document_record record) => Document.Reconstitute(
        record.Id,
        record.KnowledgeBaseId,
        record.FileName,
        record.StoragePath,
        record.Size,
        record.Status,
        record.ChunkCount,
        record.FailureReason);
}