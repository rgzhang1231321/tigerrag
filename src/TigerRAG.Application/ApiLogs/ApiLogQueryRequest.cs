namespace TigerRAG.Application.ApiLogs;

/// <summary>日志查询请求：时间范围、级别、RequestId、关键词过滤 + 分页。</summary>
public sealed record ApiLogQueryRequest(
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Level,
    string? RequestId,
    string? Keyword,
    int Page,
    int PageSize);