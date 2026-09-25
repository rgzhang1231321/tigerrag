using Microsoft.EntityFrameworkCore;
using TigerRAG.Application.ApiLogs;
using TigerRAG.Infrastructure.Persistence;

namespace TigerRAG.Infrastructure.ApiLogs.Dal;

/// <summary>日志查询 DAL：用 EF Core 从 api_log 表读取消息日志与访问日志（kind 判别）。</summary>
public sealed class ApiLogDal(TigerRagDbContext dbContext) : IApiLogDal
{
    /// <summary>按条件查询日志条目，返回分页结果。kind 过滤区分消息行与访问行，两套筛选字段互不干扰。</summary>
    public async Task<ApiLogQueryResult> ListAsync(ApiLogQueryRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var query = dbContext.ApiLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Kind))
            query = query.Where(e => e.Kind == request.Kind);
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
        if (!string.IsNullOrWhiteSpace(request.UserName))
            query = query.Where(e => EF.Functions.ILike(e.UserName!, $"%{request.UserName}%"));
        if (!string.IsNullOrWhiteSpace(request.PathKeyword))
            // 路径关键词同时命中合并路径（"POST /api/x?y=1"）或 action（"Controller.Action"）。
            query = query.Where(e =>
                EF.Functions.ILike(e.RequestPath!, $"%{request.PathKeyword}%") ||
                EF.Functions.ILike(e.Action!, $"%{request.PathKeyword}%"));
        if (request.StatusCode.HasValue)
            query = query.Where(e => e.StatusCode == request.StatusCode.Value);

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
                e.SourceContext,
                e.RequestPath!,
                e.Message,
                e.Exception,
                e.ElapsedMs,
                e.Kind,
                e.UserName,
                e.Action,
                e.StatusCode,
                e.RequestBody,
                e.ResponseBody))
            .ToListAsync(cancellationToken);

        return new ApiLogQueryResult(entries, total);
    }
}
