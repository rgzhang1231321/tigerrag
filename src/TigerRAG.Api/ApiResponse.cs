namespace TigerRAG.Api;

/// <summary>
/// 所有业务 API 的统一响应结构。
/// </summary>
public sealed record ApiResponse<T>(int Code, string Message, T? Data) : ApiResponseMarker
{
    public static ApiResponse<T> Success(T? data, string message = "success") =>
        new(0, message, data);

    public static ApiResponse<T> Failure(int code, string message, T? data = default) =>
        new(code, message, data);
}

/// <summary>
/// 统一响应的类型推断工厂。
/// </summary>
public static class ApiResponse
{
    public static ApiResponse<T> Success<T>(T? data, string message = "success") =>
        ApiResponse<T>.Success(data, message);

    public static ApiResponse<T> Failure<T>(int code, string message, T? data = default) =>
        ApiResponse<T>.Failure(code, message, data);
}

/// <summary>
/// 框架错误映射到业务响应时使用的基础错误代码。
/// </summary>
public static class ApiErrorCodes
{
    public const int Validation = 40000;
    public const int Unauthorized = 40100;
    public const int Forbidden = 40300;
    public const int NotFound = 40400;
    public const int NotImplemented = 50100;
    public const int InternalServerError = 50000;

    public static int FromHttpStatus(int statusCode) => statusCode switch
    {
        400 => Validation,
        401 => Unauthorized,
        403 => Forbidden,
        404 => NotFound,
        501 => NotImplemented,
        _ => InternalServerError
    };
}
