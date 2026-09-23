using Microsoft.Extensions.Options;
using TigerRAG.Application.Documents;
using TigerRAG.Application.Documents.Lifecycle;
using TigerRAG.Infrastructure.Queue;

namespace TigerRAG.Worker;

/// <summary>
/// 文档索引 Worker：消费 Redis 队列，启动恢复 + 定时超时恢复。
/// 每次消费在 <see cref="IServiceScope"/> 内执行，避免 Scoped DAL/UoW 被单例持有。
/// </summary>
public sealed class DocumentIndexingWorker(
    IServiceProvider serviceProvider,
    IDocumentIndexQueue queue,
    ILogger<DocumentIndexingWorker> logger,
    IOptions<DocumentIndexQueueOptions> options) : BackgroundService
{
    private readonly TimeSpan _dequeueTimeout = TimeSpan.FromSeconds(5);
    private readonly TimeSpan _processingTimeout = TimeSpan.FromSeconds(
        options.Value.ProcessingTimeoutSeconds <= 0 ? 300 : options.Value.ProcessingTimeoutSeconds);
    private readonly TimeSpan _recoveryInterval = TimeSpan.FromSeconds(
        options.Value.RecoveryScanIntervalSeconds <= 0 ? 60 : options.Value.RecoveryScanIntervalSeconds);

    private Timer? _recoveryTimer;

    /// <summary>BackgroundService 主循环：启动恢复 → 定时恢复 → 阻塞出队消费。异常吞掉以维持循环存活，仅停止令牌取消时退出。</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("TigerRAG document indexing worker started");

        // 启动恢复：扫描 Pending + 超时 Processing 文档重新入队。
        await RecoverAsync(stoppingToken);

        // 定时恢复扫描（运行时补偿）。
        _recoveryTimer = new Timer(
            _ => _ = RecoverAsync(stoppingToken),
            null,
            _recoveryInterval,
            _recoveryInterval);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var docId = await queue.DequeueAsync(_dequeueTimeout, stoppingToken);
                    if (docId is null) continue;

                    using var scope = serviceProvider.CreateScope();
                    var lifecycleDal = scope.ServiceProvider.GetRequiredService<IDocumentLifecycleDal>();
                    var indexing = scope.ServiceProvider.GetRequiredService<DocumentIndexingService>();

                    var claimed = await lifecycleDal.TryClaimAsync(docId.Value, DateTimeOffset.UtcNow, stoppingToken);
                    if (!claimed)
                    {
                        logger.LogDebug("文档 {DocId} 已被其他 Worker 认领，跳过。", docId);
                        continue;
                    }

                    try
                    {
                        await indexing.IndexAsync(docId.Value, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "索引编排异常 Document={DocId}", docId);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Worker 消费循环异常");
                    try { await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }
        finally
        {
            if (_recoveryTimer is not null)
            {
                await _recoveryTimer.DisposeAsync();
            }
            logger.LogInformation("TigerRAG document indexing worker stopped");
        }
    }

    /// <summary>
    /// 扫描所有 Pending 文档重新入队；扫描 UpdatedAt 早于阈值的 Processing 文档重置为 Pending 并入队。
    /// </summary>
    private async Task RecoverAsync(CancellationToken ct)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var queryDal = scope.ServiceProvider.GetRequiredService<IDocumentQueryDal>();
            var lifecycleDal = scope.ServiceProvider.GetRequiredService<IDocumentLifecycleDal>();
            var now = DateTimeOffset.UtcNow;

            // 启动恢复：Pending 文档重新入队。
            var pendingIds = await queryDal.ListPendingIdsAsync(ct);
            foreach (var id in pendingIds)
            {
                try { await queue.EnqueueAsync(id, ct); }
                catch (Exception ex) { logger.LogWarning(ex, "恢复入队失败 Document={DocId}", id); }
            }

            // 超时恢复：Processing 文档超过租约时间视为僵死，重置并重新入队。
            var threshold = now - _processingTimeout;
            var processingIds = await queryDal.ListTimedOutProcessingIdsAsync(threshold, ct);
            foreach (var id in processingIds)
            {
                try
                {
                    var reset = await lifecycleDal.ResetProcessingToPendingAsync(id, now, ct);
                    if (reset)
                    {
                        await queue.EnqueueAsync(id, ct);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "超时恢复失败 Document={DocId}", id);
                }
            }

            if (pendingIds.Count > 0 || processingIds.Count > 0)
            {
                logger.LogInformation("Worker 恢复：Pending={Pending}, Processing超时={Processing}",
                    pendingIds.Count, processingIds.Count);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Worker 恢复扫描异常");
        }
    }
}