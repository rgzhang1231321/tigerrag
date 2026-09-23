namespace TigerRAG.Application.Statistics.Reports;

/// <summary>报表请求。</summary>
public sealed record ReportRequest(ReportType ReportType, DateRange DateRange);