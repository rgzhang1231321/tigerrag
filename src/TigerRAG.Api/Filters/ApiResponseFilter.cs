using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using TigerRAG.Api.Common;
using TigerRAG.Infrastructure.Logging;

namespace TigerRAG.Api.Filters;

/// <summary>
/// 把控制器返回的任意结果统一包成 <see cref="ApiResponse{T}"/>，并把 RequestIdMiddleware 注入的 RequestId 写回响应体。
/// </summary>
public sealed class ApiResponseFilter(ILogger<ApiResponseFilter> logger) : IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(
        ResultExecutingContext context,
        ResultExecutionDelegate next)
    {
        var requestId = context.HttpContext.Items[RequestIdKeys.ItemKey]?.ToString() ?? string.Empty;

        if (context.Result is ObjectResult objectResult)
        {
            // 必须在改写 StatusCode=200 之前捕获原始状态码：自动 400 的 ValidationProblemDetails 走这里，
            // 改写后真实状态码再无处可寻。
            var originalStatus = objectResult.StatusCode ?? StatusCodes.Status200OK;
            objectResult.Value = InjectRequestId(WrapObjectResult(objectResult, logger), requestId);
            objectResult.StatusCode = StatusCodes.Status200OK;
            StashFailureForAccessLog(context.HttpContext, objectResult.Value, originalStatus);
        }
        else if (context.Result is StatusCodeResult statusCodeResult)
        {
            var failureCode = ApiResponse.FromHttpStatus(statusCodeResult.StatusCode);
            var failureMessage = MessageForStatus(statusCodeResult.StatusCode);
            context.Result = new ObjectResult(InjectRequestId(
                ApiResponse<object?>.Failure(failureCode, failureMessage),
                requestId))
            {
                StatusCode = StatusCodes.Status200OK
            };
            // StatusCodeResult 分支同样暂存真实状态码给访问日志。
            context.HttpContext.Items[AccessLogKeys.ItemKey] = new AccessLogFailure(
                statusCodeResult.StatusCode, failureCode, failureMessage);
        }

        await next();
    }

    /// <summary>
    /// 包装后信封为业务失败（code != Success）时，把真实状态与 code/message 暂存到
    /// HttpContext.Items，供 AccessLogMiddleware 在请求结束时记录；成功请求不暂存。
    /// </summary>
    private static void StashFailureForAccessLog(HttpContext httpContext, object wrapped, int originalStatus)
    {
        if (wrapped is ApiResponseMarker { Code: not FlagStatesOption.Success } marker)
        {
            httpContext.Items[AccessLogKeys.ItemKey] = new AccessLogFailure(
                originalStatus, marker.Code, marker.Message);
        }
    }

    private static object InjectRequestId(object wrapped, string requestId) =>
        wrapped is ApiResponseMarker marker ? marker.WithRequestId(requestId) : wrapped;

    internal static object WrapObjectResult(ObjectResult result, ILogger logger)
    {
        if (result.Value is ApiResponseMarker) return result.Value;

        if (result.Value is ValidationProblemDetails validation)
        {
            var firstError = validation.Errors.Values.SelectMany(m => m).FirstOrDefault();
            var message = firstError ?? validation.Title ?? "请求参数无效";
            logger.LogWarning("Validation failure: {FieldErrors}",
                string.Join("; ", validation.Errors.Select(kv => $"{kv.Key}={string.Join(",", kv.Value)}")));
            return ApiResponse<object?>.Failure(FlagStatesOption.Validation, message, validation.Errors);
        }

        if (result.Value is ProblemDetails problem)
        {
            var statusCode = problem.Status ?? result.StatusCode ?? StatusCodes.Status500InternalServerError;
            return ApiResponse<object?>.Failure(
                ApiResponse.FromHttpStatus(statusCode),
                problem.Detail ?? problem.Title ?? MessageForStatus(statusCode));
        }

        return ApiResponse<object?>.Success(result.Value);
    }

    private static string MessageForStatus(int statusCode) => statusCode switch
    {
        400 => "请求参数无效",
        401 => "未授权访问",
        403 => "无权访问该资源",
        404 => "资源不存在",
        501 => "功能尚未实现",
        _ => "服务内部错误"
    };

    /// <summary>按 HTTP 状态码返回默认错误消息；供中间件在无异常兜底时使用。</summary>
    internal static string DefaultMessageForStatus(int statusCode) => MessageForStatus(statusCode);
}