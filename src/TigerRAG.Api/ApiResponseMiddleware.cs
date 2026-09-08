using System.Text.Json;
using System.Text.Json.Serialization;

namespace TigerRAG.Api;

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
        try
        {
            await next(context);
        }
        catch (Exception error)
        {
            logger.LogError(error, "Unhandled API exception for {Path}", context.Request.Path);
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
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/json; charset=utf-8";
        var response = ApiResponse<object?>.Failure(
            ApiErrorCodes.FromHttpStatus(originalStatusCode),
            "请求处理失败");
        await JsonSerializer.SerializeAsync(
            originalBody,
            response,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.Never
            },
            context.RequestAborted);
    }
}
