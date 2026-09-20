namespace TigerRAG.Infrastructure.Persistence.Entities;

/// <summary>api_log 持久化记录，与 deploy/sql/003_api_log.sql 表结构一致。</summary>
public sealed class api_log_record
{
    public long Id { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string Level { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;
    public string? SourceContext { get; set; }
    public string? RequestPath { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Exception { get; set; }
    public int ElapsedMs { get; set; }
}
