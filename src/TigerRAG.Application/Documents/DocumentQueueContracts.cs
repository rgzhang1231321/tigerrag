namespace TigerRAG.Application.Documents;

/// <summary>文档索引任务队列端口；Redis List 实现（FIFO），DB 是任务状态真相源。</summary>
public interface IDocumentIndexQueue
{
    /// <summary>入队（RPUSH doc:index:queue docId）。失败仅日志，由 Worker 启动恢复扫描 Pending 兜底。</summary>
    Task EnqueueAsync(Guid documentId, CancellationToken cancellationToken);

    /// <summary>阻塞出队（BLPOP timeout）。timeout 内未取到返回 null。</summary>
    Task<Guid?> DequeueAsync(TimeSpan timeout, CancellationToken cancellationToken);
}
