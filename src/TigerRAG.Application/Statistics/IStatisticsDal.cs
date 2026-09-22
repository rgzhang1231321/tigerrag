using TigerRAG.Application.Statistics.Dashboard;
using TigerRAG.Application.Statistics.Reports;

namespace TigerRAG.Application.Statistics;

/// <summary>统计 DAL 端口：聚合 Dashboard 所需的全部指标。实现位于 Infrastructure。</summary>
public interface IStatisticsDal
{
    /// <summary>聚合 Dashboard 所需的核心指标和趋势数据。</summary>
    Task<DashboardMetricsResponse> GetDashboardMetricsAsync(CancellationToken cancellationToken);

    /// <summary>获取报表数据。</summary>
    Task<ReportDataWrapper> GetReportAsync(ReportRequest request, CancellationToken cancellationToken);

    /// <summary>导出报表为 CSV。</summary>
    Task<string> ExportReportAsync(ReportRequest request, CancellationToken cancellationToken);
}