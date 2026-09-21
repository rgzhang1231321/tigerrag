namespace TigerRAG.Application.ApiLogs;

/// <summary>日志条目视图：api_log 表一行的前端展示形态。</summary>
public sealed record ApiLogEntryDto(
    long Id,
    DateTimeOffset Timestamp,
    string Level,
    string RequestId,
    string SourceContext,
    string RequestPath,
    string Message,
    string? Exception,
    int ElapsedMs);

/// <summary>日志查询请求：时间范围、级别、RequestId、关键词过滤 + 分页。</summary>
public sealed record ApiLogQueryRequest(
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Level,
    string? RequestId,
    string? Keyword,
    int Page,
    int PageSize);

/// <summary>日志查询结果：分页列表 + 总数。</summary>
public sealed record ApiLogQueryResult(
    IReadOnlyList<ApiLogEntryDto> Entries,
    int Total);

/// <summary>日志查询 DAL 端口：从 api_log 表读取日志条目。</summary>
public interface IApiLogDal
{
    /// <summary>按条件查询日志条目，返回分页结果。</summary>
    Task<ApiLogQueryResult> ListAsync(ApiLogQueryRequest request, CancellationToken cancellationToken);
}
