namespace TigerRAG.Infrastructure.Persistence.Entities;

public sealed class MessageRecord
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public required string Role { get; set; }
    public required string Content { get; set; }
    public int? TokenCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
