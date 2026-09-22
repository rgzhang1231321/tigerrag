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