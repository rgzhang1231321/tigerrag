using System.Text.Json;
using System.Text.Json.Serialization;
using TigerRAG.Api.Common;
using TigerRAG.Infrastructure.Logging;

namespace TigerRAG.Api.Middleware;

/// <summary>
/// 兜底处理认证、路由和异常等 MVC 之外的 API 响应，确保 HTTP 状态为 200。
/// </summary>
public sealed class ApiResponseMiddleware(RequestDelegate next, ILogger<ApiResponseMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        var requestId = context.Items[RequestIdKeys.ItemKey]?.ToString() ?? string.Empty;
        try
        {
            await next(context);
        }
        catch (Exception error)
        {
            // 兜底层异常：带堆栈 LogError + requestId，保证前端报错时只凭 id 就能查到完整堆栈。
            // UseExceptionHandler 上的 LoggingExceptionHandler 仍会作为更高一层的兜底（覆盖未来新增中间件）。
            logger.LogError(
                error,
                "Unhandled API exception [RequestId={RequestId}] for {Method} {Path}: {ErrorMessage}",
                requestId,
                context.Request.Method,
                context.Request.Path,
                error.Message);
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        }

        buffer.Position = 0;
        if (context.Response.StatusCode == StatusCodes.Status200OK)
        {
            await buffer.CopyToAsync(originalBody, context.RequestAborted);
            context.Response.Body = originalBody;
            return;
        }

        var originalStatusCode = context.Response.StatusCode;
        context.Response.Body = originalBody;
        context.Response.Clear();
        // Clear 会清掉 RequestIdMiddleware 注入的 X-Request-Id，重新挂上保持链路追踪连续。
        context.Response.Headers[RequestIdKeys.ResponseHeader] = requestId;
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/json; charset=utf-8";
        var response = ApiResponse<object?>.Failure(
            ApiResponse.FromHttpStatus(originalStatusCode),
            "请求处理失败");
        var withRequestId = response.WithRequestId(requestId);
        await JsonSerializer.SerializeAsync(
            originalBody,
            withRequestId,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.Never
            },
            context.RequestAborted);
    }
}