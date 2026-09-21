namespace TigerRAG.Infrastructure.Persistence.Entities.Conversations;

/// <summary>会话持久化记录。<see cref="LastMessageAt"/> 用于列表排序，删除用户时受 FK Restrict 保护。</summary>
public sealed class conversation_record
{
    /// <summary>会话主键。</summary>
    public Guid Id { get; set; }

    /// <summary>会话所属用户 Id。</summary>
    public Guid UserId { get; set; }

    /// <summary>会话标题，默认为首条问题摘要，可由用户修改。</summary>
    public required string Title { get; set; }

    /// <summary>创建时间。</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>最后一条消息时间，作为列表默认排序键。</summary>
    public DateTimeOffset LastMessageAt { get; set; }
}
