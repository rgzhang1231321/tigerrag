using Microsoft.EntityFrameworkCore;
using TigerRAG.Application.ApiLogs;
using TigerRAG.Infrastructure.Persistence;

namespace TigerRAG.Infrastructure.ApiLogs.Dal;

/// <summary>访问日志查询 DAL：用 EF Core 从 api_access_log 表读取访问条目。</summary>
public sealed class AccessLogDal(TigerRagDbContext dbContext) : IAccessLogDal
{
    /// <summary>按条件查询访问日志条目，返回分页结果。</summary>
    public async Task<AccessLogQueryResult> ListAsync(AccessLogQueryRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var query = dbContext.AccessLogs.AsQueryable();

        if (request.From.HasValue)
            query = query.Where(e => e.Timestamp >= request.From.Value);
        if (request.To.HasValue)
            query = query.Where(e => e.Timestamp <= request.To.Value);
        if (!string.IsNullOrWhiteSpace(request.UserName))
            query = query.Where(e => EF.Functions.ILike(e.UserName!, $"%{request.UserName}%"));
        if (!string.IsNullOrWhiteSpace(request.PathKeyword))
            // 路径关键词同时匹配请求路径与 action，方便按控制器名或路径片段检索。
            query = query.Where(e =>
                EF.Functions.ILike(e.RequestPath, $"%{request.PathKeyword}%") ||
                EF.Functions.ILike(e.Action!, $"%{request.PathKeyword}%"));
        if (request.StatusCode.HasValue)
            query = query.Where(e => e.StatusCode == request.StatusCode.Value);
        if (!string.IsNullOrWhiteSpace(request.RequestId))
            query = query.Where(e => e.RequestId == request.RequestId);

        var total = await query.CountAsync(cancellationToken);

        var entries = await query
            .OrderByDescending(e => e.Timestamp)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(e => new AccessLogEntryDto(
                e.Id,
                e.Timestamp,
                e.RequestId,
                e.UserId,
                e.UserName,
                e.HttpMethod,
                e.RequestPath,
                e.QueryString,
                e.Action,
                e.RequestBody,
                e.ResponseBody,
                e.StatusCode,
                e.ElapsedMs,
                e.Ip))
            .ToListAsync(cancellationToken);

        return new AccessLogQueryResult(entries, total);
    }
}
