using Microsoft.Extensions.Hosting;

namespace TigerRAG.Infrastructure.Logging;

/// <summary>
/// 按 <see cref="AccessLogConfiguration.FlushInterval"/> 周期调用 <see cref="AccessLogBuffer.TryFlush"/>；
/// 进程退出时再 flush 一次，把残余条目落库。
/// </summary>
public sealed class AccessLogFlusherService(AccessLogConfiguration configuration) : BackgroundService
{
    /// <summary>定时批量落库访问日志，直到停止令牌触发。</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(configuration.FlushInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                AccessLogBuffer.TryFlush();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>停止时做最终 flush，尽量不丢缓冲里的条目。</summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        AccessLogBuffer.TryFlush();
    }
}
