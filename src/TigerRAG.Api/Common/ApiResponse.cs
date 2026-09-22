namespace TigerRAG.Api.Common;

/// <summary>
/// 统一响应的类型推断工厂与 HTTP 状态码到业务码的映射。
/// </summary>
public static class ApiResponse
{
    public static ApiResponse<T> Success<T>(T? data, string message = "success") =>
        ApiResponse<T>.Success(data, message);

    public static ApiResponse<T> Failure<T>(FlagStatesOption code, string message, T? data = default) =>
        ApiResponse<T>.Failure(code, message, data);

    /// <summary>框架层状态码转业务码：保留以便认证/授权过滤器统一映射。</summary>
    public static FlagStatesOption FromHttpStatus(int statusCode) => statusCode switch
    {
        400 => FlagStatesOption.Validation,
        401 => FlagStatesOption.Unauthorized,
        403 => FlagStatesOption.Forbidden,
        404 => FlagStatesOption.NotFound,
        501 => FlagStatesOption.NotImplemented,
        _ => FlagStatesOption.InternalServerError
    };
}