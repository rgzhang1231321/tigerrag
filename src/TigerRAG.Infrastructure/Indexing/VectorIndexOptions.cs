namespace TigerRAG.Infrastructure.Indexing;

/// <summary>向量集合策略。Shared：所有 KB 共用一个 collection；PerKnowledgeBase：每个 KB 一个 collection。</summary>
public enum VectorCollectionStrategy
{
    /// <summary>所有 KB 写入同一 collection，按 payload 中的 kb_id 过滤。开发期与中小规模推荐。</summary>
    Shared = 0,

    /// <summary>每个 KB 一个独立 collection；按命名模板 + kb_id 生成 collection 名。适合 KB 数量可控但单 KB 文档量大的场景。</summary>
    PerKnowledgeBase = 1,
}

/// <summary>向量索引选项；与具体实现（Qdrant/Milvus/Weaviate）解耦。</summary>
public sealed class VectorIndexOptions
{
    /// <summary>配置节名。</summary>
    public const string DefaultSectionName = "Services:Qdrant";

    /// <summary>集合策略；默认 Shared 保持当前行为。</summary>
    public VectorCollectionStrategy Strategy { get; set; } = VectorCollectionStrategy.Shared;

    /// <summary>Shared 策略下的 Qdrant Collection 名称。</summary>
    public string Collection { get; set; } = "tigerrag_documents";

    /// <summary>PerKnowledgeBase 策略下的 collection 命名模板；{0} 替换为 kbId。</summary>
    public string CollectionNameTemplate { get; set; } = "tigerrag_kb_{0}";

    /// <summary>向量维度；与 Embedding 输出维度对齐。</summary>
    public int Dimensions { get; set; } = 1536;
}
