namespace TigerRAG.Application.Statistics.Reports;

/// <summary>报表数据包装（用于序列化）。</summary>
public sealed record ReportDataWrapper(ReportType Type, object Data);