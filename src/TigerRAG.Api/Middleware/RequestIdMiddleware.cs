using TigerRAG.Infrastructure.Logging;

namespace TigerRAG.Api.Middleware;

/// <summary>
/// 为每次请求生成 RequestId：写入 <c>HttpContext.Items</c> 给后续中间件/过滤器用，
/// 同时以 <c>X-Request-Id</c> 响应头回传，方便跨链路追踪。
/// </summary>
public sealed class RequestIdMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var requestId = Guid.NewGuid().ToString("N");
        context.Items[RequestIdKeys.ItemKey] = requestId;
        context.Response.Headers[RequestIdKeys.ResponseHeader] = requestId;
        await next(context);
    }
}