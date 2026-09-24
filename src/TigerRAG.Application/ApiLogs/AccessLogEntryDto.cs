namespace TigerRAG.Application.ApiLogs;

/// <summary>访问日志条目视图：api_access_log 表一行的前端展示形态。</summary>
public sealed record AccessLogEntryDto(
    long Id,
    DateTimeOffset Timestamp,
    string RequestId,
    Guid? UserId,
    string? UserName,
    string HttpMethod,
    string RequestPath,
    string? QueryString,
    string? Action,
    string? RequestBody,
    string? ResponseBody,
    int StatusCode,
    int ElapsedMs,
    string? Ip);
