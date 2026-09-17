namespace TigerRAG.Infrastructure.Persistence.Entities;

/// <summary>知识库持久化记录。Owner 删除受 FK Restrict 保护，避免误删带文档的 KB。</summary>
public sealed class knowledge_base_record
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public Guid OwnerId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
