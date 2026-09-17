namespace TigerRAG.Infrastructure.Logging;

/// <summary>
/// 跨层共享的 RequestId 契约键：API 中间件把 RequestId 写到 HttpContext.Items[ItemKey]，
/// 日志组件从这里读出，保证前后端 RequestId 与日志条目 request_id 严格一致。
/// </summary>
public static class RequestIdKeys
{
    public const string ItemKey = "TigerRAG.RequestId";

    public const string ResponseHeader = "X-Request-Id";
}