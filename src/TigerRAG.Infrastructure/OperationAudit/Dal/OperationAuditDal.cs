using Microsoft.EntityFrameworkCore;
using TigerRAG.Application.OperationAudit;
using TigerRAG.Application.Shared;
using TigerRAG.Infrastructure.Persistence;
using TigerRAG.Infrastructure.Persistence.Entities.OperationAudit;

namespace TigerRAG.Infrastructure.OperationAudit.Dal;

/// <summary>审计读写 DAL。</summary>
public sealed class OperationAuditDal(TigerRagDbContext dbContext) : IOperationAuditDal, IOperationAuditWriter
{
    public async Task RecordAsync(OperationAuditEntry entry, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var record = new operation_audit_record
        {
            ActorId = entry.ActorId,
            ActorName = entry.ActorName,
            Action = entry.Action,
            TargetType = entry.TargetType,
            TargetId = entry.TargetId,
            Summary = entry.Summary,
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.OperationAudits.Add(record);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<OperationAuditQueryResult> QueryAsync(
        ActorContext actor,
        OperationAuditQueryRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var query = dbContext.OperationAudits.AsQueryable();

        if (request.From.HasValue)
            query = query.Where(e => e.CreatedAt >= request.From.Value);
        if (request.To.HasValue)
            query = query.Where(e => e.CreatedAt <= request.To.Value);
        if (request.ActorId.HasValue)
            query = query.Where(e => e.ActorId == request.ActorId.Value);
        if (!string.IsNullOrWhiteSpace(request.Action))
            query = query.Where(e => e.Action == request.Action);
        if (!string.IsNullOrWhiteSpace(request.Keyword))
            query = query.Where(e => EF.Functions.ILike(e.Summary, $"%{request.Keyword}%"));

        var total = await query.CountAsync(cancellationToken);

        var entries = await query
            .OrderByDescending(e => e.CreatedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(e => new OperationAuditEntryDto(
                e.Id,
                e.CreatedAt,
                e.ActorId,
                e.ActorName,
                e.Action,
                e.TargetType,
                e.TargetId,
                e.Summary))
            .ToListAsync(cancellationToken);

        return new OperationAuditQueryResult(entries, total);
    }
}
