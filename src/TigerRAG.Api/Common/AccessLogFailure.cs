namespace TigerRAG.Api.Common;

/// <summary>
/// 访问日志失败暂存载荷：真实 HTTP 状态码 + 业务码 + 失败消息。
/// 由 ApiResponseFilter / ApiResponseMiddleware 在响应改写为 200 之前写入 HttpContext.Items，
/// AccessLogMiddleware 在请求结束时读取并落库。
/// </summary>
public sealed record AccessLogFailure(int StatusCode, FlagStatesOption Code, string Message);
