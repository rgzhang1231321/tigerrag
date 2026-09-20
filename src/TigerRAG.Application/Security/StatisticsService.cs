namespace TigerRAG.Application.Security;

/// <summary>Dashboard 统计服务：直接转发 DAL 聚合结果，无额外业务规则。</summary>
public sealed class StatisticsService(IStatisticsDal statisticsDal) : IStatisticsService
{
    /// <summary>获取 Dashboard 聚合指标。</summary>
    public Task<DashboardMetricsResponse> GetDashboardMetricsAsync(CancellationToken cancellationToken) =>
        statisticsDal.GetDashboardMetricsAsync(cancellationToken);
}
