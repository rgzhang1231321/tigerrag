using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace TigerRAG.Infrastructure.Logging;

/// <summary>
/// 把每条日志转成 <see cref="ApiLogEntry"/>，从 <see cref="IHttpContextAccessor"/> 提取 RequestId 与路径，
/// 入队到 <see cref="ApiLogBuffer"/>，由后台服务批量写入 <c>api_log</c>。
/// </summary>
public sealed class TigerRagSqlLogger(string category, IHttpContextAccessor accessor, ApiLogConfiguration configuration) : ILogger
{
    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) =>
        configuration.LogLevelToEnableMap.TryGetValue(logLevel, out var enabled) && enabled;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var context = accessor.HttpContext;
        var requestId = context?.Items[RequestIdKeys.ItemKey]?.ToString() ?? string.Empty;
        var requestPath = context is null
            ? string.Empty
            : $"{context.Request.Method} {context.Request.Path}";

        ApiLogBuffer.Enqueue(new ApiLogEntry(
            Timestamp: DateTimeOffset.UtcNow,
            Level: logLevel,
            RequestId: requestId,
            SourceContext: category,
            RequestPath: requestPath,
            Message: formatter(state, exception),
            Exception: exception?.ToString(),
            ElapsedMs: 0));
    }
}