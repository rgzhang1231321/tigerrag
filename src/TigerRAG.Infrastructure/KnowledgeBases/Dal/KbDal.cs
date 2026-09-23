using Microsoft.EntityFrameworkCore;
using TigerRAG.Application.Documents.Lifecycle;
using TigerRAG.Application.KnowledgeBases;
using TigerRAG.Domain.KnowledgeBases;
using TigerRAG.Infrastructure.Persistence;
using TigerRAG.Infrastructure.Persistence.Entities.KnowledgeBases;

namespace TigerRAG.Infrastructure.KnowledgeBases.Dal;

/// <summary>知识库 EF DAL 实现；负责 knowledge_base_record ↔ KnowledgeBase 领域对象映射。</summary>
public sealed class KbDal(TigerRagDbContext dbContext) : IKbDal
{
    public async Task<KnowledgeBase?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        var record = await dbContext.KnowledgeBases
            .AsNoTracking()
            .FirstOrDefaultAsync(kb => kb.Id == id, cancellationToken);
        return record is null ? null : ToDomain(record);
    }

    public async Task<IReadOnlyList<KnowledgeBaseSummary>> ListAsync(
        Guid actorId,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        var query = dbContext.KnowledgeBases.AsNoTracking();
        if (!isAdmin)
        {
            query = query.Where(kb => kb.OwnerId == actorId);
        }

        var records = await query
            .OrderByDescending(kb => kb.CreatedAt)
            .ToListAsync(cancellationToken);

        var ownerIds = records.Select(kb => kb.OwnerId).Distinct().ToList();
        var ownerNames = await dbContext.Users
            .Where(u => ownerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.UserName ?? "unknown", cancellationToken);

        var kbIds = records.Select(kb => kb.Id).ToList();
        var docCounts = await dbContext.Documents
            .Where(d => kbIds.Contains(d.KnowledgeBaseId))
            .GroupBy(d => d.KnowledgeBaseId)
            .Select(g => new { KbId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.KbId, x => x.Count, cancellationToken);

        return records.Select(kb => new KnowledgeBaseSummary(
            kb.Id,
            kb.Name,
            kb.Description,
            kb.OwnerId,
            ownerNames.GetValueOrDefault(kb.OwnerId),
            docCounts.GetValueOrDefault(kb.Id),
            kb.CreatedAt)).ToList();
    }

    public async Task InsertAsync(KnowledgeBase knowledgeBase, CancellationToken cancellationToken)
    {
        await dbContext.KnowledgeBases.AddAsync(ToRecord(knowledgeBase), cancellationToken);
    }

    public Task UpdateAsync(KnowledgeBase knowledgeBase, CancellationToken cancellationToken)
    {
        dbContext.KnowledgeBases.Update(ToRecord(knowledgeBase));
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<Guid>> ListDocumentIdsAsync(Guid kbId, CancellationToken cancellationToken)
    {
        return await dbContext.Documents
            .Where(d => d.KnowledgeBaseId == kbId)
            .Select(d => d.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid kbId, CancellationToken cancellationToken)
    {
        await dbContext.KnowledgeBases
            .Where(kb => kb.Id == kbId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ListNonProcessingDocumentIdsAsync(
        Guid kbId,
        CancellationToken cancellationToken)
    {
        return await dbContext.Documents
            .Where(d => d.KnowledgeBaseId == kbId && d.Status != Domain.Documents.DocumentStatus.Processing)
            .Select(d => d.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<int> ResetNonProcessingToPendingAsync(
        Guid kbId,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken)
    {
        return dbContext.Documents
            .Where(d => d.KnowledgeBaseId == kbId && d.Status != Domain.Documents.DocumentStatus.Processing)
            .ExecuteUpdateAsync(d => d
                .SetProperty(x => x.Status, Domain.Documents.DocumentStatus.Pending)
                .SetProperty(x => x.UpdatedAt, updatedAt),
                cancellationToken);
    }

    /// <summary>把 EF 实体行映射为领域对象；跳过业务不变量校验（数据已过校验入库）。</summary>
    private static KnowledgeBase ToDomain(knowledge_base_record record)
    {
        return KnowledgeBase.Reconstitute(record.Id, record.Name, record.Description, record.OwnerId, record.CreatedAt);
    }

    /// <summary>把领域对象映射为 EF 实体行；保留原始 Id（由工厂生成）。</summary>
    private static knowledge_base_record ToRecord(KnowledgeBase kb)
    {
        // knowledge_base_record 的 Id 由工厂生成，持久化时需要保留原始 Id。
        return new knowledge_base_record
        {
            Id = kb.Id,
            Name = kb.Name,
            Description = kb.Description,
            OwnerId = kb.OwnerId,
            CreatedAt = kb.CreatedAt,
        };
    }
}
