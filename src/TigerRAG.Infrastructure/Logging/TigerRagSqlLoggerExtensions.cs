using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Configuration;

namespace TigerRAG.Infrastructure.Logging;

/// <summary>
/// <see cref="TigerRagSqlLoggerProvider"/> 的注册扩展入口。
/// </summary>
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