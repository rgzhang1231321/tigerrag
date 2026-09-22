using TigerRAG.Application.Statistics.Dashboard;

namespace TigerRAG.Application.Statistics.Reports;

/// <summary>用户活跃报表数据。</summary>
public sealed record UsersReportData(
    IReadOnlyList<DailyCount> NewUserTrend,
    IReadOnlyList<DailyCount> ActiveUserTrend,
    IReadOnlyList<RoleCount> RoleDistribution);