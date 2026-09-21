namespace TigerRAG.Infrastructure.Persistence.Entities.KnowledgeBases;

/// <summary>知识库持久化记录。Owner 删除受 FK Restrict 保护，避免误删带文档的 KB。</summary>
public sealed class knowledge_base_record
{
    /// <summary>知识库主键。</summary>
    public Guid Id { get; set; }

    /// <summary>知识库展示名。</summary>
    public required string Name { get; set; }

    /// <summary>知识库描述（可空）。</summary>
    public string? Description { get; set; }

    /// <summary>拥有者用户 Id。</summary>
    public Guid OwnerId { get; set; }

    /// <summary>创建时间。</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
