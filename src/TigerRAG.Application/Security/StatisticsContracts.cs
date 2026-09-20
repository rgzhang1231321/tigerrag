namespace TigerRAG.Application.Security;

/// <summary>Dashboard 指标响应。字段命名稳定，供前端直接消费。</summary>
public sealed record DashboardMetricsResponse(
    int KnowledgeBaseCount,
    int DocumentCount,
    int IndexedDocumentCount,
    int ProcessingDocumentCount,
    int FailedDocumentCount,
    int UserCount,
    int ConversationCount,
    int MessageCount,
    long TotalTokens,
    IReadOnlyList<DailyCount> RecentWeekDocuments,
    IReadOnlyList<KbDocumentCount> DocumentsByKb,
    IReadOnlyList<DailyCount> MessagesPerDay);

/// <summary>按天聚合的计数。</summary>
public sealed record DailyCount(DateTimeOffset Date, int Count);

/// <summary>知识库文档数分布。</summary>
public sealed record KbDocumentCount(Guid KnowledgeBaseId, string KnowledgeBaseName, int DocumentCount);

/// <summary>报表类型。</summary>
public enum ReportType
{
    /// <summary>文档统计。</summary>
    Documents = 1,

    /// <summary>用户活跃。</summary>
    Users = 2,

    /// <summary>对话分析。</summary>
    Conversations = 3,

    /// <summary>系统健康。</summary>
    System = 4,
}

/// <summary>日期范围。</summary>
public sealed record DateRange(DateTimeOffset Start, DateTimeOffset End)
{
    /// <summary>天数。</summary>
    public int Days => (int)(End - Start).TotalDays + 1;
}

/// <summary>报表请求。</summary>
public sealed record ReportRequest(ReportType ReportType, DateRange DateRange);

/// <summary>报表数据包装（用于序列化）。</summary>
public sealed record ReportDataWrapper(ReportType Type, object Data);
public sealed record DocumentsReportData(
    IReadOnlyList<DailyCount> UploadTrend,
    IReadOnlyList<StatusCount> StatusBreakdown,
    IReadOnlyList<KbDocumentCount> ByKb,
    IReadOnlyList<FailureItem> Failures);

/// <summary>状态计数。</summary>
public sealed record StatusCount(string Status, int Count);

/// <summary>失败项。</summary>
public sealed record FailureItem(Guid DocumentId, string DocumentTitle, string Reason, DateTimeOffset FailedAt);

/// <summary>用户活跃报表数据。</summary>
public sealed record UsersReportData(
    IReadOnlyList<DailyCount> NewUserTrend,
    IReadOnlyList<DailyCount> ActiveUserTrend,
    IReadOnlyList<RoleCount> RoleDistribution);

/// <summary>角色计数。</summary>
public sealed record RoleCount(string Role, int Count);

/// <summary>对话分析报表数据。</summary>
public sealed record ConversationsReportData(
    IReadOnlyList<DailyCount> ConversationTrend,
    IReadOnlyList<DailyCount> MessageTrend,
    double AvgMessagesPerConversation,
    IReadOnlyList<DailyCount> TokenTrend);

/// <summary>系统健康报表数据。</summary>
public sealed record SystemReportData(
    double IndexingSuccessRate,
    double FailureRate,
    IReadOnlyList<DailyCount> ApiCallTrend,
    double AvgProcessTimeSeconds);

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
