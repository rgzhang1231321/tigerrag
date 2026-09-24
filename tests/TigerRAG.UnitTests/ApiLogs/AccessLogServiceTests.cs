using TigerRAG.Application.ApiLogs;

namespace TigerRAG.UnitTests.ApiLogs;

/// <summary>访问日志查询服务单元测试：验证查询参数透传与结果映射。</summary>
public sealed class AccessLogServiceTests
{
    [Fact]
    public async Task ListAsync_WithFilters_PassesCorrectParametersToDal()
    {
        var dal = new RecordingAccessLogDal();
        var service = new AccessLogService(dal);

        var request = new AccessLogQueryRequest(
            From: new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            To: new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero),
            UserName: "alice",
            PathKeyword: "users",
            StatusCode: 401,
            RequestId: "req-123",
            Page: 2,
            PageSize: 20);

        await service.ListAsync(request, CancellationToken.None);

        Assert.NotNull(dal.LastRequest);
        Assert.Equal(request.From, dal.LastRequest!.From);
        Assert.Equal(request.To, dal.LastRequest.To);
        Assert.Equal("alice", dal.LastRequest.UserName);
        Assert.Equal("users", dal.LastRequest.PathKeyword);
        Assert.Equal(401, dal.LastRequest.StatusCode);
        Assert.Equal("req-123", dal.LastRequest.RequestId);
        Assert.Equal(2, dal.LastRequest.Page);
        Assert.Equal(20, dal.LastRequest.PageSize);
    }

    [Fact]
    public async Task ListAsync_ReturnsMappedResults()
    {
        var userId = Guid.NewGuid();
        var entries = new[]
        {
            new AccessLogEntryDto(
                Id: 1,
                Timestamp: new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero),
                RequestId: "req-1",
                UserId: userId,
                UserName: "alice",
                HttpMethod: "POST",
                RequestPath: "/api/users/list",
                QueryString: null,
                Action: "Users.List",
                RequestBody: "{\"page\":1}",
                ResponseBody: null,
                StatusCode: 200,
                ElapsedMs: 42,
                Ip: "203.0.113.7"),
        };
        var dal = new RecordingAccessLogDal { Entries = entries, Total = 1 };
        var service = new AccessLogService(dal);

        var result = await service.ListAsync(
            new AccessLogQueryRequest(null, null, null, null, null, null, 1, 20),
            CancellationToken.None);

        Assert.Equal(1, result.Total);
        Assert.Single(result.Entries);
        Assert.Equal("req-1", result.Entries[0].RequestId);
        Assert.Equal(userId, result.Entries[0].UserId);
        Assert.Equal("alice", result.Entries[0].UserName);
        Assert.Equal("Users.List", result.Entries[0].Action);
        Assert.Equal(200, result.Entries[0].StatusCode);
        Assert.Equal(42, result.Entries[0].ElapsedMs);
    }

    [Fact]
    public async Task ListAsync_EmptyResult_ReturnsEmptyList()
    {
        var dal = new RecordingAccessLogDal { Entries = [], Total = 0 };
        var service = new AccessLogService(dal);

        var result = await service.ListAsync(
            new AccessLogQueryRequest(null, null, null, null, null, null, 1, 20),
            CancellationToken.None);

        Assert.Equal(0, result.Total);
        Assert.Empty(result.Entries);
    }

    /// <summary>记录调用参数的桩 DAL。</summary>
    private sealed class RecordingAccessLogDal : IAccessLogDal
    {
        public AccessLogEntryDto[] Entries { get; set; } = [];
        public int Total { get; set; }
        public AccessLogQueryRequest? LastRequest { get; private set; }

        public Task<AccessLogQueryResult> ListAsync(
            AccessLogQueryRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new AccessLogQueryResult(Entries, Total));
        }
    }
}
