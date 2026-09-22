using TigerRAG.Application.Statistics.Dashboard;

namespace TigerRAG.Application.Statistics.Reports;

/// <summary>文档报表数据。</summary>
public sealed record DocumentsReportData(
    IReadOnlyList<DailyCount> UploadTrend,
    IReadOnlyList<StatusCount> StatusBreakdown,
    IReadOnlyList<KbDocumentCount> ByKb,
    IReadOnlyList<FailureItem> Failures);