namespace TigerRAG.Infrastructure.Persistence.Entities.ApiLogs;

/// <summary>api_log 持久化记录，与 deploy/sql/003_api_log.sql 表结构一致。</summary>
public sealed class api_log_record
{
    /// <summary>日志主键（bigserial，单调递增）。</summary>
    public long Id { get; set; }

    /// <summary>日志写入时间（应用侧时间戳）。</summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>日志级别（Information/Warning/Error 等）。</summary>
    public string Level { get; set; } = string.Empty;

    /// <summary>请求唯一标识，由中间件生成并在响应头透出，便于全链路排查。</summary>
    public string RequestId { get; set; } = string.Empty;

    /// <summary>触发源（命名空间.类名），定位日志来自哪个组件。</summary>
    public string? SourceContext { get; set; }

    /// <summary>请求路径（含 query string）。</summary>
    public string? RequestPath { get; set; }

    /// <summary>日志正文（已结构化序列化）。</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>异常详情（含堆栈），Level=Error 时填写。</summary>
    public string? Exception { get; set; }

    /// <summary>请求总耗时（毫秒）。</summary>
    public int ElapsedMs { get; set; }
}
