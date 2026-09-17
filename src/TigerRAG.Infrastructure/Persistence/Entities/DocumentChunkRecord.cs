using NpgsqlTypes;

namespace TigerRAG.Infrastructure.Persistence.Entities;

/// <summary>文档分块持久化记录。<see cref="SearchVector"/> 由 Postgres 触发器维护，GIN 索引加速全文检索。</summary>
public sealed class document_chunk_record
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public int Position { get; set; }
    public int? PageNumber { get; set; }
    public string? Title { get; set; }
    public required string Content { get; set; }
    public NpgsqlTsVector? SearchVector { get; set; }
}
