namespace TigerRAG.Infrastructure.Logging;

/// <summary>
/// 一条访问日志对应的数据库行：每个 /api 请求一行，由 AccessLogMiddleware 产生、
/// <see cref="AccessLogBuffer"/> 批量写入 <c>api_access_log</c>。
/// </summary>
public sealed record AccessLogEntry(
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
