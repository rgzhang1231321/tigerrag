using Microsoft.EntityFrameworkCore;
using TigerRAG.Application.ApiLogs;
using TigerRAG.Infrastructure.Persistence;

namespace TigerRAG.Infrastructure.ApiLogs.Dal;

/// <summary>日志查询 DAL：用 EF Core 从 api_log 表读取日志条目。</summary>
public sealed class ApiLogDal(TigerRagDbContext dbContext) : IApiLogDal
{
    /// <summary>按条件查询日志条目，返回分页结果。</summary>
    public async Task<ApiLogQueryResult> ListAsync(ApiLogQueryRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var query = dbContext.ApiLogs.AsQueryable();

        if (request.From.HasValue)
            query = query.Where(e => e.Timestamp >= request.From.Value);
        if (request.To.HasValue)
            query = query.Where(e => e.Timestamp <= request.To.Value);
        if (!string.IsNullOrWhiteSpace(request.Level))
            query = query.Where(e => e.Level == request.Level);
        if (!string.IsNullOrWhiteSpace(request.RequestId))
            query = query.Where(e => e.RequestId == request.RequestId);
        if (!string.IsNullOrWhiteSpace(request.Keyword))
            query = query.Where(e => EF.Functions.ILike(e.Message, $"%{request.Keyword}%"));

        var total = await query.CountAsync(cancellationToken);

        var entries = await query
            .OrderByDescending(e => e.Timestamp)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(e => new ApiLogEntryDto(
                e.Id,
                e.Timestamp,
                e.Level,
                e.RequestId,
                e.SourceContext!,
                e.RequestPath!,
                e.Message,
                e.Exception,
                e.ElapsedMs))
            .ToListAsync(cancellationToken);

        return new ApiLogQueryResult(entries, total);
    }
}
