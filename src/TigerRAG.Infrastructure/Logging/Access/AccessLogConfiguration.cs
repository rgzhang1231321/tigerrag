namespace TigerRAG.Infrastructure.Logging;

/// <summary>
/// 访问日志行为开关与批量参数；生产可通过 appsettings.json 的 <c>ApiAccessLog</c> 节调整。
/// </summary>
public sealed class AccessLogConfiguration
{
    /// <summary>总开关：false 时中间件直通，不产生任何访问日志。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>请求体最多记录的字符数；超出截断并追加截断标记。</summary>
    public int MaxRequestBodyChars { get; set; } = 4000;

    /// <summary>失败响应正文最多记录的字符数。</summary>
    public int MaxResponseBodyChars { get; set; } = 2000;

    /// <summary>缓存满多少条后触发批量 INSERT；访问日志每请求一行，需高于消息日志的吞吐配置。</summary>
    public int BatchSize { get; set; } = 500;

    /// <summary>缓冲容量上限。超出按"丢最旧"策略，避免 DB 长时间不可达时内存失控。</summary>
    public int Capacity { get; set; } = 5000;

    /// <summary>定时 flush 兜底，避免条目长时间压在内存里。</summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>连接串键名；解析时优先取此键，缺省回落 <c>ConnectionStrings:PostgreSql</c>。</summary>
    public string ConnectionStringKey { get; set; } = "Logging";
}
