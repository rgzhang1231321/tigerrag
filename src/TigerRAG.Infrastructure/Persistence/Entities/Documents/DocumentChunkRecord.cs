using NpgsqlTypes;

namespace TigerRAG.Infrastructure.Persistence.Entities.Documents;

/// <summary>文档分块持久化记录。<see cref="SearchVector"/> 由 Postgres 触发器维护，GIN 索引加速全文检索。</summary>
public sealed class document_chunk_record
{
    /// <summary>分块主键。</summary>
    public Guid Id { get; set; }

    /// <summary>所属文档 Id。</summary>
    public Guid DocumentId { get; set; }

    /// <summary>分块在文档中的顺序号（从 0 开始）。</summary>
    public int Position { get; set; }

    /// <summary>来源页码（PDF 等结构化文档；可空表示无页码概念）。</summary>
    public int? PageNumber { get; set; }

    /// <summary>分块标题（来自切片算法识别；可空）。</summary>
    public string? Title { get; set; }

    /// <summary>分块文本内容，作为向量化和检索的源文本。</summary>
    public required string Content { get; set; }

    /// <summary>全文检索向量，由 Postgres 触发器基于 <see cref="Content"/> 自动维护。</summary>
    public NpgsqlTsVector? SearchVector { get; set; }
}
