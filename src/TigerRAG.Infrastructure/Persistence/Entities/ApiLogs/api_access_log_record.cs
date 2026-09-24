namespace TigerRAG.Infrastructure.Persistence.Entities.ApiLogs;

/// <summary>api_access_log 持久化记录，与 deploy/sql/021_api_access_log.sql 表结构一致。</summary>
public sealed class api_access_log_record
{
    /// <summary>访问日志主键（bigserial，单调递增）。</summary>
    public long Id { get; set; }

    /// <summary>请求到达时间（应用侧时间戳）。</summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>请求唯一标识，与 api_log.request_id 对应，便于跨表排错。</summary>
    public string RequestId { get; set; } = string.Empty;

    /// <summary>操作用户 Id；匿名请求（登录/刷新）为 NULL。</summary>
    public Guid? UserId { get; set; }

    /// <summary>操作用户名。</summary>
    public string? UserName { get; set; }

    /// <summary>HTTP 方法（GET/POST 等）。</summary>
    public string HttpMethod { get; set; } = string.Empty;

    /// <summary>请求路径（不含 query string）。</summary>
    public string RequestPath { get; set; } = string.Empty;

    /// <summary>query string 原文（含 ? 前缀）。</summary>
    public string? QueryString { get; set; }

    /// <summary>命中的 Controller.Action；未匹配路由（404）为 NULL。</summary>
    public string? Action { get; set; }

    /// <summary>脱敏并截断后的请求体；无请求体为 NULL。</summary>
    public string? RequestBody { get; set; }

    /// <summary>失败响应正文（"[业务码] 消息"）；成功请求为 NULL。</summary>
    public string? ResponseBody { get; set; }

    /// <summary>改写为 200 之前的真实 HTTP 状态码。</summary>
    public int StatusCode { get; set; }

    /// <summary>请求总耗时（毫秒）。</summary>
    public int ElapsedMs { get; set; }

    /// <summary>客户端 IP；反向代理场景下是代理 IP（项目不信任 X-Forwarded-For）。</summary>
    public string? Ip { get; set; }
}
