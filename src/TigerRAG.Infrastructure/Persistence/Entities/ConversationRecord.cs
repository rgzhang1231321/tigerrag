namespace TigerRAG.Infrastructure.Persistence.Entities;

public sealed class ConversationRecord
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string Title { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastMessageAt { get; set; }
}
