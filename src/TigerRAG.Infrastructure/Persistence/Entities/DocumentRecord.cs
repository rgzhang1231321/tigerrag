using TigerRAG.Domain.Documents;

namespace TigerRAG.Infrastructure.Persistence.Entities;

/// <summary>文档持久化记录。Status 以字符串形式入库，便于手工排查；状态机不变量由 Domain 保证。</summary>
public sealed class document_record
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
