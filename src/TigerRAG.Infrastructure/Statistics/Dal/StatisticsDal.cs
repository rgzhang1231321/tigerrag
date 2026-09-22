using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using TigerRAG.Application.Shared;
using TigerRAG.Application.Statistics;
using TigerRAG.Application.Statistics.Dashboard;
using TigerRAG.Application.Statistics.Reports;
using TigerRAG.Application.Users;
using TigerRAG.Domain.Documents;
using TigerRAG.Infrastructure.Persistence;

namespace TigerRAG.Infrastructure.Statistics.Dal;

/// <summary>统计 DAL：聚合 Dashboard 所需的全部指标。顺序 await，DbContext 非线程安全。</summary>
public sealed class StatisticsDal(
    TigerRagDbContext dbContext,
    IConnectionMultiplexer redis) : IStatisticsDal
{
    private static DateTimeOffset UtcNow => new DateTimeOffset(DateTime.UtcNow, TimeSpan.Zero);
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

        var startDate = new DateTimeOffset(DateTime.UtcNow.AddDays(-6).Date, TimeSpan.Zero);
        var recentWeekDocuments = await dbContext.Documents
            .AsNoTracking()
            .Where(d => d.CreatedAt >= startDate)
            .Select(d => d.CreatedAt)
            .ToListAsync(cancellationToken);

        var documentsByKb = await dbContext.Documents
            .AsNoTracking()
            .Select(d => d.KnowledgeBaseId)
            .ToListAsync(cancellationToken);

        var kbGrouped = documentsByKb
            .GroupBy(d => d)
            .Select(g => new { KbId = g.Key, Count = g.Count() })
            .ToList();

        var kbNames = await dbContext.KnowledgeBases
            .AsNoTracking()
            .Where(kb => kbGrouped.Select(r => r.KbId).Contains(kb.Id))
            .ToDictionaryAsync(kb => kb.Id, kb => kb.Name, cancellationToken);

        var messagesPerDay = await dbContext.Messages
            .AsNoTracking()
            .Where(m => m.CreatedAt >= startDate)
            .Select(m => m.CreatedAt)
            .ToListAsync(cancellationToken);

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
            RecentWeekDocuments: recentWeekDocuments
                .GroupBy(d => d.Date)
                .Select(g => new DailyCount(g.Key, g.Count()))
                .OrderBy(m => m.Date)
                .ToList(),
            DocumentsByKb: kbGrouped
                .Select(r => new KbDocumentCount(
                    r.KbId,
                    kbNames.TryGetValue(r.KbId, out var name) ? name : string.Empty,
                    r.Count))
                .ToList(),
            MessagesPerDay: messagesPerDay
                .GroupBy(m => m.Date)
                .Select(g => new DailyCount(g.Key, g.Count()))
                .OrderBy(x => x.Date)
                .ToList());
    }

    /// <summary>获取报表数据（带 Redis 缓存）。</summary>
    public async Task<ReportDataWrapper> GetReportAsync(ReportRequest request, CancellationToken cancellationToken)
    {
        var cacheKey = $"statistics:report:{request.ReportType}:{request.DateRange.Start:yyyyMMdd}:{request.DateRange.End:yyyyMMdd}";
        try
        {
            var db = redis.GetDatabase();
            var cached = await db.StringGetAsync(cacheKey);
            if (cached.HasValue)
            {
                return System.Text.Json.JsonSerializer.Deserialize<ReportDataWrapper>(cached.ToString())!;
            }
        }
        catch (RedisException)
        {
            // Redis 不可用时跳过缓存，直接计算。
        }

        ReportDataWrapper result = request.ReportType switch
        {
            ReportType.Documents => new ReportDataWrapper(request.ReportType, await GetDocumentsReportAsync(request.DateRange, cancellationToken)),
            ReportType.Users => new ReportDataWrapper(request.ReportType, await GetUsersReportAsync(request.DateRange, cancellationToken)),
            ReportType.Conversations => new ReportDataWrapper(request.ReportType, await GetConversationsReportAsync(request.DateRange, cancellationToken)),
            ReportType.System => new ReportDataWrapper(request.ReportType, await GetSystemReportAsync(request.DateRange, cancellationToken)),
            _ => throw new ArgumentOutOfRangeException(nameof(request.ReportType))
        };

        try
        {
            var db = redis.GetDatabase();
            var json = System.Text.Json.JsonSerializer.Serialize(result);
            await db.StringSetAsync(cacheKey, json, TimeSpan.FromMinutes(5));
        }
        catch (RedisException)
        {
            // Redis 不可用时忽略缓存写入。
        }

        return result;
    }

    /// <summary>导出报表为 CSV（绕过缓存，确保数据最新）。</summary>
    public async Task<string> ExportReportAsync(ReportRequest request, CancellationToken cancellationToken)
    {
        ReportDataWrapper wrapper = request.ReportType switch
        {
            ReportType.Documents => new ReportDataWrapper(request.ReportType, await GetDocumentsReportAsync(request.DateRange, cancellationToken)),
            ReportType.Users => new ReportDataWrapper(request.ReportType, await GetUsersReportAsync(request.DateRange, cancellationToken)),
            ReportType.Conversations => new ReportDataWrapper(request.ReportType, await GetConversationsReportAsync(request.DateRange, cancellationToken)),
            ReportType.System => new ReportDataWrapper(request.ReportType, await GetSystemReportAsync(request.DateRange, cancellationToken)),
            _ => throw new ArgumentOutOfRangeException(nameof(request.ReportType))
        };

        var lines = new List<string>();

        lines.Add($"报表类型,{request.ReportType}");
        lines.Add($"日期范围,{request.DateRange.Start:yyyy-MM-dd} 至 {request.DateRange.End:yyyy-MM-dd}");
        lines.Add(string.Empty);

        switch (wrapper.Data)
        {
            case DocumentsReportData d:
                lines.Add("日期,上传数");
                foreach (var item in d.UploadTrend)
                    lines.Add($"{item.Date:yyyy-MM-dd},{item.Count}");
                lines.Add(string.Empty);
                lines.Add("状态,数量");
                foreach (var item in d.StatusBreakdown)
                    lines.Add($"{item.Status},{item.Count}");
                break;

            case UsersReportData u:
                lines.Add("日期,新增用户数");
                foreach (var item in u.NewUserTrend)
                    lines.Add($"{item.Date:yyyy-MM-dd},{item.Count}");
                lines.Add(string.Empty);
                lines.Add("日期,活跃用户数");
                foreach (var item in u.ActiveUserTrend)
                    lines.Add($"{item.Date:yyyy-MM-dd},{item.Count}");
                break;

            case ConversationsReportData c:
                lines.Add("日期,对话数,消息数,Token数");
                for (var i = 0; i < c.ConversationTrend.Count; i++)
                    lines.Add($"{c.ConversationTrend[i].Date:yyyy-MM-dd},{c.ConversationTrend[i].Count},{c.MessageTrend[i].Count},{c.TokenTrend[i].Count}");
                lines.Add(string.Empty);
                lines.Add($"平均消息数/对话,{c.AvgMessagesPerConversation:F2}");
                break;

            case SystemReportData s:
                lines.Add("指标,值");
                lines.Add($"索引成功率,{s.IndexingSuccessRate:P2}");
                lines.Add($"失败率,{s.FailureRate:P2}");
                lines.Add($"平均处理时间(秒),{s.AvgProcessTimeSeconds:F2}");
                lines.Add(string.Empty);
                lines.Add("日期,API调用数");
                foreach (var item in s.ApiCallTrend)
                    lines.Add($"{item.Date:yyyy-MM-dd},{item.Count}");
                break;
        }

        return string.Join("\n", lines);
    }

    private async Task<DocumentsReportData> GetDocumentsReportAsync(DateRange range, CancellationToken ct)
    {
        var docs = dbContext.Documents.AsNoTracking().Where(d => d.CreatedAt >= range.Start && d.CreatedAt <= range.End);

        var docList = await docs.ToListAsync(ct);

        var uploadTrend = docList
            .GroupBy(d => d.CreatedAt.Date)
            .Select(g => new DailyCount(new DateTimeOffset(g.Key, TimeSpan.Zero), g.Count()))
            .OrderBy(x => x.Date)
            .ToList();

        var statusBreakdown = docList
            .GroupBy(d => d.Status.ToString())
            .Select(g => new StatusCount(g.Key, g.Count()))
            .ToList();

        var byKb = docList
            .GroupBy(d => d.KnowledgeBaseId)
            .Select(g => new { KbId = g.Key, Count = g.Count() })
            .ToList();

        var kbNames = await dbContext.KnowledgeBases
            .AsNoTracking()
            .Where(kb => byKb.Select(r => r.KbId).Contains(kb.Id))
            .ToDictionaryAsync(kb => kb.Id, kb => kb.Name, ct);

        var kbDocumentCounts = byKb
            .Select(r => new KbDocumentCount(r.KbId, kbNames.GetValueOrDefault(r.KbId, string.Empty), r.Count))
            .ToList();

        var failures = docList
            .Where(d => d.Status == DocumentStatus.Failed)
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => new FailureItem(d.Id, d.FileName, d.FailureReason ?? "未知错误", d.CreatedAt))
            .Take(100)
            .ToList();

        return new DocumentsReportData(uploadTrend, statusBreakdown, kbDocumentCounts, failures);
    }

    private async Task<UsersReportData> GetUsersReportAsync(DateRange range, CancellationToken ct)
    {
        var users = await dbContext.Users
            .AsNoTracking()
            .Where(u => u.CreatedAt >= range.Start && u.CreatedAt <= range.End)
            .ToListAsync(ct);

        var newUserTrend = users
            .GroupBy(u => u.CreatedAt.Date)
            .Select(g => new DailyCount(new DateTimeOffset(g.Key, TimeSpan.Zero), g.Count()))
            .OrderBy(x => x.Date)
            .ToList();

        var conversations = await dbContext.Conversations
            .AsNoTracking()
            .Where(c => c.CreatedAt >= range.Start && c.CreatedAt <= range.End)
            .ToListAsync(ct);

        var activeUserTrend = conversations
            .GroupBy(c => c.CreatedAt.Date)
            .Select(g => new DailyCount(new DateTimeOffset(g.Key, TimeSpan.Zero), g.Select(c => c.UserId).Distinct().Count()))
            .OrderBy(x => x.Date)
            .ToList();

        var userIds = users.Select(u => u.Id).ToHashSet();

        List<IdentityUserRole<Guid>> userRoles = userIds.Count == 0
            ? []
            : await dbContext.UserRoles
                .Where(ur => userIds.Contains(ur.UserId))
                .ToListAsync(ct);

        var roles = await dbContext.Roles
            .AsNoTracking()
            .ToListAsync(ct);

        var roleDistribution = userRoles
            .Join(roles,
                ur => ur.RoleId,
                r => r.Id,
                (ur, r) => new { ur.UserId, Role = r.Name ?? string.Empty })
            .GroupBy(x => x.Role)
            .Select(g => new RoleCount(g.Key, g.Select(x => x.UserId).Distinct().Count()))
            .OrderByDescending(x => x.Count)
            .ToList();

        return new UsersReportData(newUserTrend, activeUserTrend, roleDistribution);
    }

    private async Task<ConversationsReportData> GetConversationsReportAsync(DateRange range, CancellationToken ct)
    {
        var conversations = await dbContext.Conversations.AsNoTracking()
            .Where(c => c.CreatedAt >= range.Start && c.CreatedAt <= range.End)
            .ToListAsync(ct);
        var messages = await dbContext.Messages.AsNoTracking()
            .Where(m => m.CreatedAt >= range.Start && m.CreatedAt <= range.End)
            .ToListAsync(ct);

        var conversationTrend = conversations
            .GroupBy(c => c.CreatedAt.Date)
            .Select(g => new DailyCount(new DateTimeOffset(g.Key, TimeSpan.Zero), g.Count()))
            .OrderBy(x => x.Date)
            .ToList();

        var messageTrend = messages
            .GroupBy(m => m.CreatedAt.Date)
            .Select(g => new DailyCount(new DateTimeOffset(g.Key, TimeSpan.Zero), g.Count()))
            .OrderBy(x => x.Date)
            .ToList();

        var totalConvs = conversations.Count;
        var totalMsgs = messages.Count;
        var avgMessages = totalConvs > 0 ? (double)totalMsgs / totalConvs : 0;

        var tokenTrend = messages
            .GroupBy(m => m.CreatedAt.Date)
            .Select(g => new DailyCount(new DateTimeOffset(g.Key, TimeSpan.Zero), (int)g.Sum(m => (long?)m.TokenCount ?? 0)))
            .OrderBy(x => x.Date)
            .ToList();

        return new ConversationsReportData(conversationTrend, messageTrend, avgMessages, tokenTrend);
    }

    private async Task<SystemReportData> GetSystemReportAsync(DateRange range, CancellationToken ct)
    {
        var docs = await dbContext.Documents.AsNoTracking()
            .Where(d => d.CreatedAt >= range.Start && d.CreatedAt <= range.End)
            .ToListAsync(ct);

        var totalDocs = docs.Count;
        var indexed = docs.Count(d => d.Status == DocumentStatus.Indexed);
        var failed = docs.Count(d => d.Status == DocumentStatus.Failed);
        var successRate = totalDocs > 0 ? (double)indexed / totalDocs : 0;
        var failureRate = totalDocs > 0 ? (double)failed / totalDocs : 0;

        var apiLogs = await dbContext.ApiLogs
            .AsNoTracking()
            .Where(a => a.Timestamp >= range.Start && a.Timestamp <= range.End)
            .Select(a => a.Timestamp)
            .ToListAsync(ct);

        var apiCallTrend = apiLogs
            .GroupBy(a => a.Date)
            .Select(g => new DailyCount(new DateTimeOffset(g.Key, TimeSpan.Zero), g.Count()))
            .OrderBy(x => x.Date)
            .ToList();

        var indexedDocs = docs.Where(d => d.Status == DocumentStatus.Indexed).ToList();
        var avgSeconds = indexedDocs.Count > 0
            ? indexedDocs.Average(d => (d.UpdatedAt - d.CreatedAt).TotalSeconds)
            : 0;

        return new SystemReportData(successRate, failureRate, apiCallTrend, avgSeconds);
    }
}
