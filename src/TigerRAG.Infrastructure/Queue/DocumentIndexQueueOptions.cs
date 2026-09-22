namespace TigerRAG.Infrastructure.Queue;

/// <summary>文档索引队列选项；与具体实现（Redis/Kafka/RabbitMQ）解耦。</summary>
public sealed class DocumentIndexQueueOptions
{
    /// <summary>配置节名。</summary>
    public const string DefaultSectionName = "DocumentIndexing";

    /// <summary>Redis List 队列键名。</summary>
    public string RedisKey { get; set; } = "doc:index:queue";
}
