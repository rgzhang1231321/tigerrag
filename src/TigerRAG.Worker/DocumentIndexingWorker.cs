namespace TigerRAG.Worker;

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
