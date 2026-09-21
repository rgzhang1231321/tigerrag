using TigerRAG.Domain.Documents;

namespace TigerRAG.Infrastructure.Persistence.Entities.Documents;

/// <summary>文档持久化记录。Status 以字符串形式入库，便于手工排查；状态机不变量由 Domain 保证。</summary>
public sealed class document_record
{
    /// <summary>文档主键。</summary>
    public Guid Id { get; set; }

    /// <summary>所属知识库 Id。</summary>
    public Guid KnowledgeBaseId { get; set; }

    /// <summary>原始文件名（含扩展名）。</summary>
    public required string FileName { get; set; }

    /// <summary>对象存储中的对象 Key（MinIO 路径）。</summary>
    public required string StoragePath { get; set; }

    /// <summary>文档处理状态；状态机由 Domain 维护，此处仅持久化。</summary>
    public DocumentStatus Status { get; set; }

    /// <summary>已切分的分块数量，索引完成后填入。</summary>
    public int ChunkCount { get; set; }

    /// <summary>处理失败原因，仅在 Status=Failed 时填写。</summary>
    public string? FailureReason { get; set; }

    /// <summary>创建者用户 Id。</summary>
    public Guid CreatedBy { get; set; }

    /// <summary>创建时间。</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>最后更新时间（状态变更、索引完成等）。</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
