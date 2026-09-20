using Microsoft.EntityFrameworkCore;
using TigerRAG.Application.Security;
using TigerRAG.Domain.Documents;
using TigerRAG.Infrastructure.Persistence;

namespace TigerRAG.Infrastructure.Dal;

/// <summary>统计 DAL：聚合 Dashboard 所需的全部指标。顺序 await，DbContext 非线程安全。</summary>
public sealed class StatisticsDal(TigerRagDbContext dbContext) : IStatisticsDal
{
    /// <summary>聚合 Dashboard 所需的核心指标和趋势数据。</summary>
    public async Task<DashboardMetricsResponse> GetDashboardMetricsAsync(CancellationToken cancellationToken)
    {
        var documents = dbContext.Documents.AsNoTracking();
        var kbCount = await dbContext.KnowledgeBases.CountAsync(cancellationToken);
        var docCount = await documents.CountAsync(cancellationToken);
        var docIndexed = await documents.CountAsync(d => d.Status == DocumentStatus.Indexed, cancellationToken);
        var docProcessing = await documents.CountAsync(d => d.Status == DocumentStatus.Processing, cancellationToken);
        var docFailed = await documents.CountAsync(d => d.Status == DocumentStatus.Failed, cancellationToken);
        var userCount = await dbContext.Users.CountAsync(cancellationToken);
        var convCount = await dbContext.Conversations.CountAsync(cancellationToken);
        var msgCount = await dbContext.Messages.CountAsync(cancellationToken);
        var totalTokens = await dbContext.Messages.SumAsync(m => (long?)m.TokenCount ?? 0, cancellationToken);

        var recentWeekDocuments = await GetRecentWeekDocumentsAsync(cancellationToken);
        var documentsByKb = await GetDocumentsByKbAsync(cancellationToken);
        var messagesPerDay = await GetMessagesPerDayAsync(cancellationToken);

        return new DashboardMetricsResponse(
            KnowledgeBaseCount: kbCount,
            DocumentCount: docCount,
            IndexedDocumentCount: docIndexed,
            ProcessingDocumentCount: docProcessing,
            FailedDocumentCount: docFailed,
            UserCount: userCount,
            ConversationCount: convCount,
            MessageCount: msgCount,
            TotalTokens: totalTokens,
            RecentWeekDocuments: recentWeekDocuments,
            DocumentsByKb: documentsByKb,
            MessagesPerDay: messagesPerDay);
    }

    /// <summary>近 7 天每日上传文档数。</summary>
    private async Task<IReadOnlyList<DailyCount>> GetRecentWeekDocumentsAsync(CancellationToken cancellationToken)
    {
        var startDate = new DateTimeOffset(DateTime.UtcNow.AddDays(-6).Date, TimeSpan.Zero);
        var docs = await dbContext.Documents
            .AsNoTracking()
            .Where(d => d.CreatedAt >= startDate)
            .Select(d => d.CreatedAt)
            .ToListAsync(cancellationToken);

        return docs
            .GroupBy(d => d.Date)
            .Select(g => new DailyCount(g.Key, g.Count()))
            .OrderBy(m => m.Date)
            .ToList();
    }

    /// <summary>各知识库文档数分布。</summary>
    private async Task<IReadOnlyList<KbDocumentCount>> GetDocumentsByKbAsync(CancellationToken cancellationToken)
    {
        var docs = await dbContext.Documents
            .AsNoTracking()
            .Select(d => d.KnowledgeBaseId)
            .ToListAsync(cancellationToken);

        var grouped = docs
            .GroupBy(d => d)
            .Select(g => new { KbId = g.Key, Count = g.Count() })
            .ToList();

        var kbNames = await dbContext.KnowledgeBases
            .AsNoTracking()
            .Where(kb => grouped.Select(r => r.KbId).Contains(kb.Id))
            .ToDictionaryAsync(kb => kb.Id, kb => kb.Name, cancellationToken);

        return grouped
            .Select(r => new KbDocumentCount(
                r.KbId,
                kbNames.TryGetValue(r.KbId, out var name) ? name : string.Empty,
                r.Count))
            .ToList();
    }

    /// <summary>近 7 天每日消息数。</summary>
    private async Task<IReadOnlyList<DailyCount>> GetMessagesPerDayAsync(CancellationToken cancellationToken)
    {
        var startDate = new DateTimeOffset(DateTime.UtcNow.AddDays(-6).Date, TimeSpan.Zero);
        var msgs = await dbContext.Messages
            .AsNoTracking()
            .Where(m => m.CreatedAt >= startDate)
            .Select(m => m.CreatedAt)
            .ToListAsync(cancellationToken);

        return msgs
            .GroupBy(m => m.Date)
            .Select(g => new DailyCount(g.Key, g.Count()))
            .OrderBy(x => x.Date)
            .ToList();
    }
}
