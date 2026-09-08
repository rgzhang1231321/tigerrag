namespace TigerRAG.Infrastructure.Persistence.Entities;

public sealed class AuditLogRecord
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string Query { get; set; }
    public Guid[] RetrievedDocumentIds { get; set; } = [];
    public string? Answer { get; set; }
    public string? IpAddress { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
