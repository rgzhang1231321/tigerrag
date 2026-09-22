using TigerRAG.Application.Statistics.Dashboard;
using TigerRAG.Application.Statistics.Reports;

namespace TigerRAG.Application.Statistics;

/// <summary>Dashboard 统计服务：直接转发 DAL 聚合结果，无额外业务规则。</summary>
public sealed class StatisticsService(IStatisticsDal statisticsDal) : IStatisticsService
{
    /// <summary>获取 Dashboard 聚合指标。</summary>
    public Task<DashboardMetricsResponse> GetDashboardMetricsAsync(CancellationToken cancellationToken) =>
        statisticsDal.GetDashboardMetricsAsync(cancellationToken);

    /// <summary>获取报表数据。</summary>
    public Task<ReportDataWrapper> GetReportAsync(ReportRequest request, CancellationToken cancellationToken) =>
        statisticsDal.GetReportAsync(request, cancellationToken);

    /// <summary>导出报表为 CSV。</summary>
    public Task<string> ExportReportAsync(ReportRequest request, CancellationToken cancellationToken) =>
        statisticsDal.ExportReportAsync(request, cancellationToken);
}
