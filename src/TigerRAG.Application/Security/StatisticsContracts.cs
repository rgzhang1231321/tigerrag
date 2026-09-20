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

/// <summary>统计 DAL 端口：聚合 Dashboard 所需的全部指标。实现位于 Infrastructure。</summary>
public interface IStatisticsDal
{
    /// <summary>聚合 Dashboard 所需的核心指标和趋势数据。</summary>
    Task<DashboardMetricsResponse> GetDashboardMetricsAsync(CancellationToken cancellationToken);
}

/// <summary>Dashboard 统计服务端口。实现仅做聚合转发，无额外业务规则。</summary>
public interface IStatisticsService
{
    /// <summary>获取 Dashboard 聚合指标。</summary>
    Task<DashboardMetricsResponse> GetDashboardMetricsAsync(CancellationToken cancellationToken);
}
