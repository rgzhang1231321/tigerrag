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

/// <summary>
/// 业务码枚举；数值与早期 ApiErrorCodes 保持一致，便于老前端按数值兼容。
/// </summary>
public enum FlagStatesOption
{
    Success = 0,
    Validation = 40000,
    Unauthorized = 40100,
    Forbidden = 40300,
    NotFound = 40400,
    Conflict = 40900,
    InternalServerError = 50000,
    NotImplemented = 50100
}

/// <summary>
/// 分页响应的便捷工厂；让 <c>HasNextPage</c>/<c>Total</c> 不必每个 controller 手动填。
/// </summary>
public static class PaginatedApiResponse
{
    public static ApiResponse<IReadOnlyCollection<T>> Of<T>(
        IReadOnlyCollection<T> data,
        int total,
        bool hasNextPage,
        string message = "success") =>
        new(string.Empty, FlagStatesOption.Success, nameof(FlagStatesOption.Success), true, message, data, hasNextPage, total);
}

/// <summary>
/// 标识已经被 ApiResponse 外壳包装过的响应对象，供过滤器去重与注入 RequestId。
/// </summary>
public interface ApiResponseMarker
{
    object WithRequestId(string requestId);
}

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