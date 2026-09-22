namespace TigerRAG.Api.Common;

/// <summary>
/// 所有业务 API 的统一响应。HTTP 状态码恒为 200；业务错误由 <see cref="FlagStatesOption"/> 表达。
/// <see cref="WithRequestId"/> 由响应过滤器调用，把每请求生成的 RequestId 注入到响应体中。
/// </summary>
public sealed record ApiResponse<T>(
    string RequestId,
    FlagStatesOption Code,
    string Value,
    bool Flag,
    string Message,
    T? Data,
    bool HasNextPage = false,
    int Total = 0) : ApiResponseMarker
{
    public object WithRequestId(string requestId) => this with { RequestId = requestId };

    public static ApiResponse<T> Success(T? data, string message = "success") =>
        new(string.Empty, FlagStatesOption.Success, nameof(FlagStatesOption.Success), true, message, data);

    public static ApiResponse<T> Failure(FlagStatesOption code, string message, T? data = default) =>
        new(string.Empty, code, code.ToString(), false, message, data);
}