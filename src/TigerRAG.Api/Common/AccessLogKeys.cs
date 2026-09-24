namespace TigerRAG.Api.Common;

/// <summary>
/// 访问日志相关的 HttpContext.Items 键名常量。
/// </summary>
public static class AccessLogKeys
{
    /// <summary>
    /// 失败信息暂存键：响应包装层（filter/middleware）写入真实状态码与业务失败信息，
    /// AccessLogMiddleware 读取后写入 api_access_log。成功请求不写入该键。
    /// </summary>
    public const string ItemKey = "TigerRAG.AccessLog.Failure";
}
