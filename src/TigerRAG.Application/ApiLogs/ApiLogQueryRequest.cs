namespace TigerRAG.Application.ApiLogs;

/// <summary>日志查询请求：kind 判别 + 时间范围/级别/RequestId/关键词（消息行）或
/// 用户名/路径关键词/状态码（访问行）过滤 + 分页。两套筛选字段按 kind 二选一使用。</summary>
public sealed record ApiLogQueryRequest(
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Level,
    string? RequestId,
    string? Keyword,
    string? Kind,
    string? UserName,
    string? PathKeyword,
    int? StatusCode,
    int Page,
    int PageSize);
