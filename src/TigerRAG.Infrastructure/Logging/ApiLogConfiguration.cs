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
        [LogLevel.Critical] = true
    };

    /// <summary>缓存满多少条后触发批量 INSERT；按调用频次预估（默认 100 条已足够覆盖 30s 窗口）。</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>定时 flush 兜底，避免长尾请求一直压在内存里。</summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>连接串键名；解析时优先取此键，缺省回落 <c>ConnectionStrings:PostgreSql</c>。</summary>
    public string ConnectionStringKey { get; set; } = "Logging";
}