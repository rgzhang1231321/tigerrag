using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using TigerRAG.Api.Common;
using TigerRAG.Infrastructure.Logging;

namespace TigerRAG.Api.Middleware;

/// <summary>
/// 为每个 /api 请求记录一条访问日志（kind='access'：合并方法路径/用户/action/真实状态码/耗时/
/// 脱敏请求体/失败响应体），入队到 <see cref="ApiLogBuffer"/> 与消息日志同表同通道批量落库。
/// 注册在 ApiResponseMiddleware 之前：请求体先读先回卷（事后读会阻塞在无人消费的流上），
/// 响应改写为 200 前的真实状态码经 <see cref="AccessLogKeys.ItemKey"/> 暂存契约传递。
/// 日志组件自身任何异常都不破坏请求主流程。
/// </summary>
public sealed class AccessLogMiddleware(
    RequestDelegate next,
    ApiLogConfiguration configuration,
    ILogger<AccessLogMiddleware> logger)
{
    /// <summary>记录请求全程：读体 → next → 落一条访问日志；防御性 catch 保持上层兜底语义。</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        if (!configuration.AccessEnabled ||
            !context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var startTimestamp = Stopwatch.GetTimestamp();
        // 请求体必须在 next 之前读：401/404 等未到 MVC 的请求无人消费 body，事后读会阻塞在未上传完的流上。
        var requestBody = await ReadRequestBodyAsync(context);

        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // 客户端已断开：响应无人接收，不重抛；按 nginx 惯例记 499。
            EnqueueAccessEntry(context, startTimestamp, requestBody, 499, "[499] 客户端已取消请求");
            return;
        }
        catch (Exception error)
        {
            // 业务异常通常已被 ApiResponseMiddleware 吞掉，这里只是防御（覆盖其自身在 try 外抛 OCE 等）：
            // 记 500 后重抛，保持 UseExceptionHandler 的兜底语义。
            EnqueueAccessEntry(
                context,
                startTimestamp,
                requestBody,
                StatusCodes.Status500InternalServerError,
                $"[50000] {error.Message}");
            throw;
        }

        EnqueueAccessEntry(context, startTimestamp, requestBody, null, null);
    }

    /// <summary>
    /// 前置读取并脱敏请求体：JSON 读上限字符后回卷（Position=0）供 MVC 绑定；
    /// 非 JSON（含 multipart）只记摘要不读体；无体返回 null。任何失败都降级为占位符，绝不抛出。
    /// </summary>
    private async Task<string?> ReadRequestBodyAsync(HttpContext context)
    {
        try
        {
            var request = context.Request;
            if (request.ContentLength is null or 0)
            {
                return null;
            }

            var contentType = request.ContentType ?? string.Empty;
            if (!contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
            {
                // multipart 等不读体（避免大文件进内存），只记媒体类型与长度摘要。
                var mediaType = contentType.Split(';')[0].Trim();
                if (mediaType.Length == 0)
                {
                    mediaType = "unknown";
                }

                return $"[{mediaType}] content-length={request.ContentLength}";
            }

            request.EnableBuffering();
            // 多读 1 个字符用于探测截断。
            var buffer = new char[configuration.MaxRequestBodyChars + 1];
            int read;
            using (var reader = new StreamReader(
                request.Body,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 1024,
                leaveOpen: true))
            {
                read = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
            }

            // 回卷给 MVC 绑定；必须先回卷再脱敏，保证脱敏抛错也不影响绑定。
            request.Body.Position = 0;
            var body = new string(buffer, 0, Math.Min(read, configuration.MaxRequestBodyChars));
            if (read > configuration.MaxRequestBodyChars)
            {
                body += "...(截断)";
            }

            var masked = SensitiveJsonMasker.Mask(body);
            return string.IsNullOrEmpty(masked) ? null : masked;
        }
        catch (Exception error)
        {
            logger.LogError(
                error,
                "读取请求体失败，访问日志降级为占位符 [RequestId={RequestId}]",
                context.Items[RequestIdKeys.ItemKey]);
            return "[request body unreadable]";
        }
    }

    /// <summary>
    /// 组装并入队一条访问条目。真实状态码与失败信息优先取暂存契约（响应已被改写为 200）；
    /// 防御路径（客户端断开/重抛）由调用方直接给定。入队自身异常只记日志，绝不抛出。
    /// </summary>
    private void EnqueueAccessEntry(
        HttpContext context,
        long startTimestamp,
        string? requestBody,
        int? forcedStatusCode,
        string? forcedResponseBody)
    {
        try
        {
            AccessLogFailure? failure = null;
            if (forcedStatusCode is null)
            {
                failure = context.Items.TryGetValue(AccessLogKeys.ItemKey, out var boxed) &&
                    boxed is AccessLogFailure stashed
                        ? stashed
                        : null;
            }

            var statusCode = forcedStatusCode ?? failure?.StatusCode ?? context.Response.StatusCode;
            var responseBody = forcedResponseBody ?? (failure is null
                ? null
                : $"[{(int)failure.Code}] {failure.Message}");
            Enqueue(context, startTimestamp, requestBody, statusCode, responseBody);
        }
        catch (Exception error)
        {
            // 响应可能已写出，这里只能吞掉并留痕。
            logger.LogError(
                error,
                "写入访问日志条目失败 [RequestId={RequestId}]",
                context.Items[RequestIdKeys.ItemKey]);
        }
    }

    /// <summary>采集全部字段并截断到列宽后入队；所有 NOT NULL 字段恒有值。</summary>
    private void Enqueue(
        HttpContext context,
        long startTimestamp,
        string? requestBody,
        int statusCode,
        string? responseBody)
    {
        var elapsedMs = (int)Math.Max(0, Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);

        // action 取命中的控制器描述；404 等未匹配路由时为 null。
        string? action = null;
        if (context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>() is { } descriptor)
        {
            action = Truncate($"{descriptor.ControllerName}.{descriptor.ActionName}", 500);
        }

        // 用户来自 JWT claims；匿名请求（登录/刷新/401）为 null。
        context.TryGetActor(out var actor);

        // 方法+路径+查询串合并进单一 request_path 列（"POST /api/x?y=1"），与消息日志的格式一致。
        var requestPath = Truncate(
            $"{context.Request.Method} {context.Request.Path}{context.Request.QueryString}",
            500);

        ApiLogBuffer.Enqueue(new ApiLogEntry(
            Timestamp: DateTimeOffset.UtcNow,
            Level: LogLevel.Information,
            RequestId: context.Items[RequestIdKeys.ItemKey]?.ToString() ?? string.Empty,
            SourceContext: null,
            RequestPath: requestPath,
            Message: $"{requestPath} {statusCode} {elapsedMs}ms",
            Exception: null,
            ElapsedMs: elapsedMs,
            Kind: "access",
            UserName: actor is null ? null : Truncate(actor.Name, 256),
            Action: action,
            StatusCode: statusCode,
            RequestBody: requestBody,
            ResponseBody: string.IsNullOrEmpty(responseBody)
                ? null
                : Truncate(responseBody, configuration.MaxResponseBodyChars)));
    }

    /// <summary>代码侧截断到数据库列宽，杜绝超长值毒化批次。</summary>
    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
