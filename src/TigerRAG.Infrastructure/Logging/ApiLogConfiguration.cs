using Microsoft.Extensions.Logging;

namespace TigerRAG.Infrastructure.Logging;

/// <summary>
/// 行为开关：级别→启用、批量容量、刷新间隔。生产可放进 appsettings.json 调整。
/// </summary>
public sealed class ApiLogConfiguration
{
    /// <summary>逐条启用矩阵；未列出的级别一律禁用，避免开发日志被自动暴露。</summary>
    public Dictionary<LogLevel, bool> LogLevelToEnableMap { get; set; } = new()
    {
        [LogLevel.Warning] = true,
        [LogLevel.Error] = true,
        [LogLevel.Critical] = true,
        [LogLevel.Information] = true
    };

    /// <summary>缓存满多少条后触发批量 INSERT；合并访问日志（每请求一条）后按其量级预估。</summary>
    public int BatchSize { get; set; } = 500;

    /// <summary>缓冲容量上限。超出按"丢最旧"策略，避免 DB 长时间不可达时内存失控。</summary>
    public int Capacity { get; set; } = 5000;

    /// <summary>定时 flush 兜底，避免长尾请求一直压在内存里。</summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>连接串键名；解析时优先取此键，缺省回落 <c>ConnectionStrings:PostgreSql</c>。</summary>
    public string ConnectionStringKey { get; set; } = "Logging";

    /// <summary>访问日志（每 /api 请求一条，kind='access'）总开关；关闭后 <see cref="AccessLogMiddleware"/> 直通。</summary>
    public bool AccessEnabled { get; set; } = true;

    /// <summary>访问日志请求体最大记录字符数；超出截断并加"...(截断)"标记。</summary>
    public int MaxRequestBodyChars { get; set; } = 4000;

    /// <summary>访问日志失败响应体（"[{code}] {message}"）最大记录字符数；超出截断。</summary>
    public int MaxResponseBodyChars { get; set; } = 2000;
}
