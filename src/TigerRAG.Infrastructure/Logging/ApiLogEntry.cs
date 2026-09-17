using Microsoft.Extensions.Logging;

namespace TigerRAG.Infrastructure.Logging;

/// <summary>
/// 一条日志对应的数据库行；只有 stack trace 与 requestId 两个字段是真正对排错不可替代的，
/// 其余字段（路径 / 耗时 / category）按需填充以降低 INSERT 体积。
/// </summary>
public sealed record ApiLogEntry(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string RequestId,
    string SourceContext,
    string RequestPath,
    string Message,
    string? Exception,
    int ElapsedMs);