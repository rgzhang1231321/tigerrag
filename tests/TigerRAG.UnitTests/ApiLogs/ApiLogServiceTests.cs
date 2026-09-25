using TigerRAG.Application.ApiLogs;

namespace TigerRAG.UnitTests.ApiLogs;

/// <summary>日志查询服务单元测试：验证查询参数（含访问筛选与 kind 判别）传递、分页与结果映射。</summary>
public sealed class ApiLogServiceTests
{
    [Fact]
    public async Task ListAsync_WithFilters_PassesCorrectParametersToDal()
    {
        var dal = new RecordingApiLogDal();
        var service = new ApiLogService(dal);

        var request = new ApiLogQueryRequest(
            From: new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            To: new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero),
            Level: "Error",
            RequestId: "req-123",
            Keyword: "timeout",
            Kind: "message",
            UserName: null,
            PathKeyword: null,
            StatusCode: null,
            Page: 1,
            PageSize: 20);

        await service.ListAsync(request, CancellationToken.None);

        Assert.NotNull(dal.LastRequest);
        Assert.Equal(request.From, dal.LastRequest!.From);
        Assert.Equal(request.To, dal.LastRequest.To);
        Assert.Equal("Error", dal.LastRequest.Level);
        Assert.Equal("req-123", dal.LastRequest.RequestId);
        Assert.Equal("timeout", dal.LastRequest.Keyword);
        Assert.Equal("message", dal.LastRequest.Kind);
        Assert.Equal(1, dal.LastRequest.Page);
        Assert.Equal(20, dal.LastRequest.PageSize);
    }

    [Fact]
    public async Task ListAsync_WithAccessFilters_PassesAccessParametersToDal()
    {
        // 访问日志 Tab 走同一服务/端点：kind='access' + 用户名/路径/状态码筛选必须原样透传。
        var dal = new RecordingApiLogDal();
        var service = new ApiLogService(dal);

        var request = new ApiLogQueryRequest(
            From: null,
            To: null,
            Level: null,
            RequestId: "req-a",
            Keyword: null,
            Kind: "access",
            UserName: "bob",
            PathKeyword: "logs",
            StatusCode: 404,
            Page: 2,
            PageSize: 10);

        await service.ListAsync(request, CancellationToken.None);

        Assert.NotNull(dal.LastRequest);
        Assert.Equal("access", dal.LastRequest!.Kind);
        Assert.Equal("bob", dal.LastRequest.UserName);
        Assert.Equal("logs", dal.LastRequest.PathKeyword);
        Assert.Equal(404, dal.LastRequest.StatusCode);
        Assert.Equal(2, dal.LastRequest.Page);
        Assert.Equal(10, dal.LastRequest.PageSize);
    }

    [Fact]
    public async Task ListAsync_ReturnsMappedResults()
    {
        var entries = new[]
        {
            new ApiLogEntryDto(
                Id: 1,
                Timestamp: new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero),
                Level: "Error",
                RequestId: "req-1",
                SourceContext: "Test",
                RequestPath: "GET /api/test",
                Message: "Something failed",
                Exception: "System.Exception: timeout",
                ElapsedMs: 150,
                Kind: "message",
                UserName: null,
                Action: null,
                StatusCode: null,
                RequestBody: null,
                ResponseBody: null),
        };
        var dal = new RecordingApiLogDal
        {
            Entries = entries,
            Total = 1,
        };
        var service = new ApiLogService(dal);

        var result = await service.ListAsync(
            new ApiLogQueryRequest(null, null, null, null, null, null, null, null, null, 1, 20),
            CancellationToken.None);

        Assert.Equal(1, result.Total);
        Assert.Single(result.Entries);
        Assert.Equal("req-1", result.Entries[0].RequestId);
        Assert.Equal("Error", result.Entries[0].Level);
        Assert.Equal("GET /api/test", result.Entries[0].RequestPath);
        Assert.Equal("Something failed", result.Entries[0].Message);
        Assert.Equal("System.Exception: timeout", result.Entries[0].Exception);
        Assert.Equal(150, result.Entries[0].ElapsedMs);
        Assert.Equal("message", result.Entries[0].Kind);
    }

    [Fact]
    public async Task ListAsync_EmptyResult_ReturnsEmptyList()
    {
        var dal = new RecordingApiLogDal
        {
            Entries = [],
            Total = 0,
        };
        var service = new ApiLogService(dal);

        var result = await service.ListAsync(
            new ApiLogQueryRequest(null, null, null, null, null, null, null, null, null, 1, 20),
            CancellationToken.None);

        Assert.Equal(0, result.Total);
        Assert.Empty(result.Entries);
    }

    private sealed class RecordingApiLogDal : IApiLogDal
    {
        public ApiLogEntryDto[] Entries { get; set; } = [];
        public int Total { get; set; }
        public ApiLogQueryRequest? LastRequest { get; private set; }

        public Task<ApiLogQueryResult> ListAsync(ApiLogQueryRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new ApiLogQueryResult(Entries, Total));
        }
    }
}
