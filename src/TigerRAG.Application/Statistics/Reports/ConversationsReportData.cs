using TigerRAG.Application.Statistics.Dashboard;

namespace TigerRAG.Application.Statistics.Reports;

/// <summary>对话分析报表数据。</summary>
public sealed record ConversationsReportData(
    IReadOnlyList<DailyCount> ConversationTrend,
    IReadOnlyList<DailyCount> MessageTrend,
    double AvgMessagesPerConversation,
    IReadOnlyList<DailyCount> TokenTrend);