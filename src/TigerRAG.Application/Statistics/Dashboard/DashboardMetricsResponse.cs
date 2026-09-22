namespace TigerRAG.Application.Statistics.Dashboard;

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