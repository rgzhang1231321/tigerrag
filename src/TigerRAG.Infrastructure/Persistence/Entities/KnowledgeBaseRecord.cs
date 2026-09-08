namespace TigerRAG.Infrastructure.Persistence.Entities;

public sealed class KnowledgeBaseRecord
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public Guid OwnerId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
