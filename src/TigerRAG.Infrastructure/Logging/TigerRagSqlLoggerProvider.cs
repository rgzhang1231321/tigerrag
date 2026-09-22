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