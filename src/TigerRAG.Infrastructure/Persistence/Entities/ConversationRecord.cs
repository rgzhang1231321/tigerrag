namespace TigerRAG.Infrastructure.Persistence.Entities;

/// <summary>会话持久化记录。<see cref="LastMessageAt"/> 用于列表排序，删除用户时受 FK Restrict 保护。</summary>
public sealed class conversation_record
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string Title { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastMessageAt { get; set; }
}
