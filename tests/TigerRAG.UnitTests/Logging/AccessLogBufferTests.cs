using Microsoft.Extensions.Configuration;
using TigerRAG.Infrastructure.Logging;

namespace TigerRAG.UnitTests.Logging;

/// <summary>
/// 验证 <see cref="AccessLogBuffer"/> 的容量上限、批量截断、字段完整性与失败可观测性。
/// 这些约束保证 DB 故障时内存可控 + 行为可观测，而不是静默丢失。
/// </summary>
[Collection(nameof(AccessLogBufferCollection))]
public sealed class AccessLogBufferTests
{
    public AccessLogBufferTests()
    {
        // 各测试间共享静态状态，必须先 reset。
        AccessLogBuffer.ResetForTest();
    }

    [Fact]
    public void Enqueue_BeyondCapacity_DropsOldestEntries()
    {
        AccessLogBuffer.Configure(
            new AccessLogConfiguration { Capacity = 100 },
            new ConfigurationBuilder().AddInMemoryCollection().Build());

        for (var i = 0; i < 150; i++)
        {
            AccessLogBuffer.Enqueue(Entry(i));
        }

        var entries = AccessLogBuffer.DrainForTest();
        Assert.Equal(100, entries.Count);
        // 前 50 条被丢弃，保留的是最近 100 条（i = 50..149）。
        Assert.Equal("req-50", entries[0].RequestId);
        Assert.Equal("req-149", entries[^1].RequestId);
    }

    [Fact]
    public void Enqueue_KeepsAllAccessFieldsIntact()
    {
        // 中间件填充的每个字段都必须原样到达缓冲，任何丢失都会让访问日志失去排查价值。
        AccessLogBuffer.Configure(
            new AccessLogConfiguration(),
            new ConfigurationBuilder().AddInMemoryCollection().Build());

        AccessLogBuffer.Enqueue(Entry(7));

        var entry = Assert.Single(AccessLogBuffer.DrainForTest());
        Assert.Equal("req-7", entry.RequestId);
        Assert.NotNull(entry.UserId);
        Assert.Equal("admin", entry.UserName);
        Assert.Equal("POST", entry.HttpMethod);
        Assert.Equal("/api/test/7", entry.RequestPath);
        Assert.Equal("?page=1", entry.QueryString);
        Assert.Equal("Test.List", entry.Action);
        Assert.Contains("keyword", entry.RequestBody);
        Assert.Null(entry.ResponseBody);
        Assert.Equal(200, entry.StatusCode);
        Assert.Equal(7, entry.ElapsedMs);
        Assert.Equal("127.0.0.1", entry.Ip);
    }

    [Fact]
    public void TryFlush_OnDbFailure_IncrementsFailureCountAndKeepsBatch()
    {
        // 连接串无效 ⇒ Open/INSERT 抛异常；条目必须保留在缓冲里，不允许静默丢。
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PostgreSql"] = "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=1",
            })
            .Build();
        AccessLogBuffer.Configure(new AccessLogConfiguration(), config);

        for (var i = 0; i < 10; i++)
        {
            AccessLogBuffer.Enqueue(Entry(i));
        }

        var before = AccessLogBuffer.FailureCount;
        var written = AccessLogBuffer.TryFlush();
        var after = AccessLogBuffer.FailureCount;

        Assert.Equal(0, written);
        Assert.True(after > before, "FailureCount must increment when DB write fails.");
        // 条目仍在缓冲里，便于下轮重试而非静默丢。
        Assert.Equal(10, AccessLogBuffer.DrainForTest().Count);
    }

    [Fact]
    public void TryFlush_RespectsBatchSize_LeavesRemainderInBuffer()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PostgreSql"] = "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=1",
            })
            .Build();
        AccessLogBuffer.Configure(new AccessLogConfiguration { BatchSize = 100 }, config);

        for (var i = 0; i < 250; i++)
        {
            AccessLogBuffer.Enqueue(Entry(i));
        }

        // 不可达 DB → TryFlush 不写；失败时整个 batch 留在缓冲里，下一轮重试；剩余必须 ≥ 150。
        AccessLogBuffer.TryFlush();
        Assert.True(AccessLogBuffer.DrainForTest().Count >= 150);
    }

    private static AccessLogEntry Entry(int sequence) => new(
        Timestamp: DateTimeOffset.UtcNow,
        RequestId: $"req-{sequence}",
        UserId: Guid.NewGuid(),
        UserName: "admin",
        HttpMethod: "POST",
        RequestPath: $"/api/test/{sequence}",
        QueryString: "?page=1",
        Action: "Test.List",
        RequestBody: """{"keyword":"kb"}""",
        ResponseBody: null,
        StatusCode: 200,
        ElapsedMs: sequence,
        Ip: "127.0.0.1");
}
