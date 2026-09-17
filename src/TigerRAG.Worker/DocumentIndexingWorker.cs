namespace TigerRAG.Worker;

/// <summary>后台消费的占位实现。完整流水线由 Application 编排，Worker 只负责 scope 创建与生命周期管理。</summary>
public sealed class DocumentIndexingWorker(ILogger<DocumentIndexingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("TigerRAG document indexing worker started");

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("TigerRAG document indexing worker stopped");
        }
    }
}
