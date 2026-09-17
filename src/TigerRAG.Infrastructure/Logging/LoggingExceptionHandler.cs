using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using TigerRAG.Infrastructure.Logging;

namespace TigerRAG.Infrastructure.Logging;

/// <summary>
/// 兜底 MVC 管道里冒上来的未处理异常：先 <see cref="ILogger.LogError"/> 一遍（含堆栈 + 当前 RequestId），
/// 再返回 <c>false</c>，让默认 ProblemDetails 写入继续走，最终由 <c>ApiResponseMiddleware</c> 包成标准 ApiResponse 信封。
/// </summary>
public sealed class LoggingExceptionHandler(ILogger<LoggingExceptionHandler> logger) : IExceptionHandler
{
    public ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var requestId = context.Items[RequestIdKeys.ItemKey]?.ToString() ?? "(none)";
        logger.LogError(
            exception,
            "Unhandled MVC exception [RequestId={RequestId}] for {Method} {Path}: {ErrorMessage}",
            requestId,
            context.Request.Method,
            context.Request.Path,
            exception.Message);
        return ValueTask.FromResult(false);
    }
}