using TigerRAG.Domain.Documents;

namespace TigerRAG.Infrastructure.Persistence.Entities;

public sealed class DocumentRecord
{
    public Guid Id { get; set; }
    public Guid KnowledgeBaseId { get; set; }
    public required string FileName { get; set; }
    public required string StoragePath { get; set; }
    public DocumentStatus Status { get; set; }
    public int ChunkCount { get; set; }
    public string? FailureReason { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
