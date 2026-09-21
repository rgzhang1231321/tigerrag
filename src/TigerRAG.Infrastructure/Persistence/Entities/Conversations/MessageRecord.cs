namespace TigerRAG.Infrastructure.Persistence.Entities.Conversations;

/// <summary>消息持久化记录。<see cref="Role"/> 取值为 User/Assistant/System。</summary>
public sealed class message_record
{
    /// <summary>消息主键。</summary>
    public Guid Id { get; set; }

    /// <summary>所属会话 Id。</summary>
    public Guid ConversationId { get; set; }

    /// <summary>消息角色（User/Assistant/System），决定渲染样式与提示词拼接位置。</summary>
    public required string Role { get; set; }

    /// <summary>消息文本内容。</summary>
    public required string Content { get; set; }

    /// <summary>LLM 计费的 Token 数（仅 Assistant 消息统计；可空）。</summary>
    public int? TokenCount { get; set; }

    /// <summary>消息创建时间。</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
