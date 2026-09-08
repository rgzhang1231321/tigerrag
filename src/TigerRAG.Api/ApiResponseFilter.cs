using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace TigerRAG.Api;

/// <summary>
/// 将 Controller 的所有结果统一转换为 HTTP 200 的 ApiResponse。
/// </summary>
public sealed class ApiResponseFilter : IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(
        ResultExecutingContext context,
        ResultExecutionDelegate next)
    {
        if (context.Result is ObjectResult objectResult)
        {
            objectResult.Value = WrapObjectResult(objectResult);
            objectResult.StatusCode = StatusCodes.Status200OK;
        }
        else if (context.Result is StatusCodeResult statusCodeResult)
        {
            context.Result = new ObjectResult(ApiResponse<object?>.Failure(
                ApiErrorCodes.FromHttpStatus(statusCodeResult.StatusCode),
                MessageForStatus(statusCodeResult.StatusCode)))
            {
                StatusCode = StatusCodes.Status200OK
            };
        }

        await next();
    }

    private static object WrapObjectResult(ObjectResult result)
    {
        if (result.Value is ApiResponseMarker)
        {
            return result.Value;
        }

        if (result.Value is ProblemDetails problem)
        {
            var statusCode = problem.Status ?? result.StatusCode ?? StatusCodes.Status500InternalServerError;
            return ApiResponse<object?>.Failure(
                ApiErrorCodes.FromHttpStatus(statusCode),
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
}

/// <summary>
/// 用于识别已经包装过的响应，避免重复嵌套。
/// </summary>
public interface ApiResponseMarker;
