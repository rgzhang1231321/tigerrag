using TigerRAG.Application.Statistics.Dashboard;
using TigerRAG.Application.Statistics.Reports;

namespace TigerRAG.Application.Statistics;

/// <summary>Dashboard 统计服务端口。实现仅做聚合转发，无额外业务规则。</summary>
public interface IStatisticsService
{
    /// <summary>获取 Dashboard 聚合指标。</summary>
    Task<DashboardMetricsResponse> GetDashboardMetricsAsync(CancellationToken cancellationToken);

    /// <summary>获取报表数据。</summary>
    Task<ReportDataWrapper> GetReportAsync(ReportRequest request, CancellationToken cancellationToken);

    /// <summary>导出报表为 CSV。</summary>
    Task<string> ExportReportAsync(ReportRequest request, CancellationToken cancellationToken);
}