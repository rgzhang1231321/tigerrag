using TigerRAG.Application.Statistics.Dashboard;

namespace TigerRAG.Application.Statistics.Reports;

/// <summary>系统健康报表数据。</summary>
public sealed record SystemReportData(
    double IndexingSuccessRate,
    double FailureRate,
    IReadOnlyList<DailyCount> ApiCallTrend,
    double AvgProcessTimeSeconds);