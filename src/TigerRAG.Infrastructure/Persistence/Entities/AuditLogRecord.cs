namespace TigerRAG.Infrastructure.Persistence.Entities;

/// <summary>审计日志持久化记录。<see cref="Query"/> / <see cref="Answer"/> 用于问答审计回放。</summary>
public sealed class audit_log_record
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string Query { get; set; }
    public Guid[] RetrievedDocumentIds { get; set; } = [];
    public string? Answer { get; set; }
    public string? IpAddress { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
