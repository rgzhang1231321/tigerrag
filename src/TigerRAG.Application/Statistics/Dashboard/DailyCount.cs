namespace TigerRAG.Application.Statistics.Dashboard;

/// <summary>按天聚合的计数。</summary>
public sealed record DailyCount(DateTimeOffset Date, int Count);