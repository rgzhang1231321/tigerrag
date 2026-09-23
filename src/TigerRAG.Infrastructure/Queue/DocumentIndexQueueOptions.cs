namespace TigerRAG.Infrastructure.Queue;

/// <summary>文档索引队列选项；与具体实现（Redis/Kafka/RabbitMQ）解耦。</summary>
public sealed class DocumentIndexQueueOptions
{
    /// <summary>配置节名。</summary>
    public const string DefaultSectionName = "DocumentIndexing";

    /// <summary>Redis List 队列键名。</summary>
    public string RedisKey { get; set; } = "doc:index:queue";

    /// <summary>Worker 处理租约超时（秒），超过该时间仍处于 Processing 的文档视为僵死，触发超时恢复。默认 300 秒（5 分钟）。</summary>
    public int ProcessingTimeoutSeconds { get; set; } = 300;

    /// <summary>Worker 定时恢复扫描间隔（秒）。默认 60 秒。</summary>
    public int RecoveryScanIntervalSeconds { get; set; } = 60;

    /// <summary>Worker 阻塞出队超时（秒）。默认 5 秒。</summary>
    public int DequeueTimeoutSeconds { get; set; } = 5;
}