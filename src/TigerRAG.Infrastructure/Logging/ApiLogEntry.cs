using Microsoft.Extensions.Logging;

namespace TigerRAG.Infrastructure.Logging;

/// <summary>
/// 一条日志对应的数据库行；只有 stack trace 与 requestId 两个字段是真正对排错不可替代的，
/// 其余字段（路径 / 耗时 / category）按需填充以降低 INSERT 体积。
/// 访问日志（<see cref="AccessLogMiddleware"/> 产出的 kind='access' 行）复用同一记录：
/// 尾部访问维度字段带默认值，消息日志构造处无需感知。
/// </summary>
public sealed record ApiLogEntry(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string RequestId,
    string? SourceContext,
    string RequestPath,
    string Message,
    string? Exception,
    int ElapsedMs,
    string Kind = "message",
    string? UserName = null,
    string? Action = null,
    int? StatusCode = null,
    string? RequestBody = null,
    string? ResponseBody = null);
