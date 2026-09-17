namespace TigerRAG.Infrastructure.Persistence.Entities;

/// <summary>消息持久化记录。<see cref="Role"/> 取值为 User/Assistant/System。</summary>
public sealed class message_record
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public required string Role { get; set; }
    public required string Content { get; set; }
    public int? TokenCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
