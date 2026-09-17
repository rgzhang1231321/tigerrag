using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Configuration;

namespace TigerRAG.Infrastructure.Logging;

/// <summary>
/// 把 <see cref="TigerRagSqlLogger"/> 注册到 <c>ILoggingBuilder</c>，并启动 <see cref="ApiLogFlusherService"/> 后台批量刷新。
/// </summary>
[ProviderAlias("TigerRagSql")]
public sealed class TigerRagSqlLoggerProvider : ILoggerProvider
{
    private readonly IHttpContextAccessor _accessor;
    private readonly ApiLogConfiguration _configuration;

    public TigerRagSqlLoggerProvider(IHttpContextAccessor accessor, ApiLogConfiguration configuration)
    {
        _accessor = accessor;
        _configuration = configuration;
    }

    public ILogger CreateLogger(string categoryName) =>
        new TigerRagSqlLogger(categoryName, _accessor, _configuration);

    public void Dispose()
    {
    }
}

public static class TigerRagSqlLoggerExtensions
{
    /// <summary>
    /// 替换默认日志提供器为 <see cref="TigerRagSqlLoggerProvider"/>：所有 <c>ILogger&lt;T&gt;</c> 走同一通道，
    /// 产物落到独立 <c>api_log</c> 表；请求级别由 <see cref="ApiLogConfiguration"/> 控制。
    /// </summary>
    public static ILoggingBuilder AddTigerRagSqlLogger(this ILoggingBuilder builder, IConfiguration configuration)
    {
        var logConfiguration = new ApiLogConfiguration();
        configuration.GetSection("ApiLog").Bind(logConfiguration);
        ApiLogBuffer.Configure(logConfiguration, configuration);

        builder.Services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        builder.Services.AddSingleton(logConfiguration);
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, TigerRagSqlLoggerProvider>());
        LoggerProviderOptions.RegisterProviderOptions<ApiLogConfiguration, TigerRagSqlLoggerProvider>(builder.Services);
        return builder;
    }
}

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