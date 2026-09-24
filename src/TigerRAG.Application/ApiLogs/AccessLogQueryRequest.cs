namespace TigerRAG.Application.ApiLogs;

/// <summary>访问日志查询请求：时间范围、用户名、路径关键词、状态码、RequestId 过滤 + 分页。</summary>
public sealed record AccessLogQueryRequest(
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? UserName,
    string? PathKeyword,
    int? StatusCode,
    string? RequestId,
    int Page,
    int PageSize);
