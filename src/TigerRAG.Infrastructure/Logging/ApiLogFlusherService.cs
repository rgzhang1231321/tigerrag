using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TigerRAG.Infrastructure.Logging;

/// <summary>
/// 按 <see cref="ApiLogConfiguration.FlushInterval"/> 周期调用 <see cref="ApiLogBuffer.TryFlush"/>；
/// 进程退出时再 flush 一次，把残余条目落库。
/// </summary>
public sealed class ApiLogFlusherService(ApiLogConfiguration configuration) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(configuration.FlushInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                ApiLogBuffer.TryFlush();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        ApiLogBuffer.TryFlush();
    }
}