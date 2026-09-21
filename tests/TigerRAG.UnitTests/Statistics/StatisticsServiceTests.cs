using Microsoft.Extensions.DependencyInjection;
using TigerRAG.Application.Statistics;
using TigerRAG.Domain.Documents;
using Xunit;

namespace TigerRAG.UnitTests.Statistics;

/// <summary>StatisticsService 契约测试：服务直接返回 DAL 聚合结果，无额外业务逻辑。</summary>
public sealed class StatisticsServiceTests
{
    [Fact]
    public async Task GetDashboardMetricsAsync_ReturnsDalResult()
    {
        var expected = new DashboardMetricsResponse(
            KnowledgeBaseCount: 3,
            DocumentCount: 42,
            IndexedDocumentCount: 30,
            ProcessingDocumentCount: 5,
            FailedDocumentCount: 2,
            UserCount: 10,
            ConversationCount: 50,
            MessageCount: 200,
            TotalTokens: 12345,
            RecentWeekDocuments:
            [
                new DailyCount(new DateTime(2026, 9, 13), 1),
                new DailyCount(new DateTime(2026, 9, 14), 2),
            ],
            DocumentsByKb:
            [
                new KbDocumentCount(Guid.NewGuid(), "知识库A", 20),
            ],
            MessagesPerDay:
            [
                new DailyCount(new DateTime(2026, 9, 13), 10),
            ]);

        var services = new ServiceCollection();
        services.AddSingleton<IStatisticsDal>(new StubStatisticsDal(expected));
        services.AddSingleton<StatisticsService>();
        using var provider = services.BuildServiceProvider();

        var service = provider.GetRequiredService<StatisticsService>();
        var result = await service.GetDashboardMetricsAsync(CancellationToken.None);

        Assert.Equal(3, result.KnowledgeBaseCount);
        Assert.Equal(42, result.DocumentCount);
        Assert.Equal(30, result.IndexedDocumentCount);
        Assert.Equal(5, result.ProcessingDocumentCount);
        Assert.Equal(2, result.FailedDocumentCount);
        Assert.Equal(10, result.UserCount);
        Assert.Equal(50, result.ConversationCount);
        Assert.Equal(200, result.MessageCount);
        Assert.Equal(12345, result.TotalTokens);
        Assert.Equal(2, result.RecentWeekDocuments.Count);
        Assert.Equal(1, result.DocumentsByKb.Count);
        Assert.Equal(1, result.MessagesPerDay.Count);
        Assert.Equal(new DateTime(2026, 9, 13), result.RecentWeekDocuments[0].Date.DateTime);
        Assert.Equal(1, result.RecentWeekDocuments[0].Count);
    }

    private sealed class StubStatisticsDal(DashboardMetricsResponse expected) : IStatisticsDal
    {
        public Task<DashboardMetricsResponse> GetDashboardMetricsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(expected);

        public Task<ReportDataWrapper> GetReportAsync(ReportRequest request, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<string> ExportReportAsync(ReportRequest request, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
    }
}
