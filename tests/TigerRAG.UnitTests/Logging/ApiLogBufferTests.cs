using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TigerRAG.Infrastructure.Logging;

namespace TigerRAG.UnitTests.Logging;

/// <summary>
/// 验证 <see cref="ApiLogBuffer"/> 的容量上限、批量截断、访问行字段完整性与失败可观测性。
/// 这些约束保证 DB 故障时内存可控 + 行为可观测，而不是静默丢失。
/// </summary>
[Collection(nameof(ApiLogBufferCollection))]
public sealed class ApiLogBufferTests
{
    public ApiLogBufferTests()
    {
        // 各测试间共享静态状态，必须先 reset。
        ApiLogBuffer.ResetForTest();
    }

    [Fact]
    public void Enqueue_BeyondCapacity_DropsOldestEntries()
    {
        ApiLogBuffer.Configure(
            new ApiLogConfiguration { Capacity = 1000 },
            new ConfigurationBuilder().AddInMemoryCollection().Build());

        for (var i = 0; i < 1500; i++)
        {
            ApiLogBuffer.Enqueue(Entry(i));
        }

        var entries = ApiLogBuffer.DrainForTest();
        Assert.Equal(1000, entries.Count);
        // 前 500 条被丢弃，保留的是最近 1000 条（i = 500..1499）。
        Assert.Equal("m-500", entries[0].Message);
        Assert.Equal("m-1499", entries[^1].Message);
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
        ApiLogBuffer.Configure(new ApiLogConfiguration(), config);

        for (var i = 0; i < 10; i++)
        {
            ApiLogBuffer.Enqueue(Entry(i));
        }

        var before = ApiLogBuffer.FailureCount;
        var written = ApiLogBuffer.TryFlush();
        var after = ApiLogBuffer.FailureCount;

        Assert.Equal(0, written);
        Assert.True(after > before, "FailureCount must increment when DB write fails.");
        // 条目仍在缓冲里，便于下轮重试而非静默丢。
        Assert.Equal(10, ApiLogBuffer.DrainForTest().Count);
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
        ApiLogBuffer.Configure(new ApiLogConfiguration { BatchSize = 100 }, config);

        for (var i = 0; i < 250; i++)
        {
            ApiLogBuffer.Enqueue(Entry(i));
        }

        // 不可达 DB → TryFlush 不写；条目按 BatchSize 截断后剩余仍保留在缓冲里。
        // 行为契约：失败时整个 batch 留在缓冲里，下一轮重试；本测试只断言剩余 ≥ 150。
        ApiLogBuffer.TryFlush();
        Assert.True(ApiLogBuffer.DrainForTest().Count >= 150);
    }

    [Fact]
    public void Enqueue_AccessEntry_KeepsAllAccessFieldsIntact()
    {
        // 访问行与消息行共用同一缓冲：kind 判别 + 访问维度字段必须原样穿过入队/取出。
        ApiLogBuffer.Configure(
            new ApiLogConfiguration(),
            new ConfigurationBuilder().AddInMemoryCollection().Build());

        ApiLogBuffer.Enqueue(new ApiLogEntry(
            Timestamp: new DateTimeOffset(2026, 9, 25, 8, 0, 0, TimeSpan.Zero),
            Level: LogLevel.Information,
            RequestId: "req-access-1",
            SourceContext: null,
            RequestPath: "POST /api/logs/list?pageSize=20",
            Message: "POST /api/logs/list?pageSize=20 200 42ms",
            Exception: null,
            ElapsedMs: 42,
            Kind: "access",
            UserName: "bob",
            Action: "ApiLogs.List",
            StatusCode: 200,
            RequestBody: """{"pageSize":20,"passwordHash":"***"}""",
            ResponseBody: null));

        var actual = Assert.Single(ApiLogBuffer.DrainForTest());
        Assert.Equal("access", actual.Kind);
        Assert.Equal(LogLevel.Information, actual.Level);
        Assert.Null(actual.SourceContext);
        Assert.Equal("POST /api/logs/list?pageSize=20", actual.RequestPath);
        Assert.Equal("POST /api/logs/list?pageSize=20 200 42ms", actual.Message);
        Assert.Equal(42, actual.ElapsedMs);
        Assert.Equal("bob", actual.UserName);
        Assert.Equal("ApiLogs.List", actual.Action);
        Assert.Equal(200, actual.StatusCode);
        Assert.Equal("""{"pageSize":20,"passwordHash":"***"}""", actual.RequestBody);
        Assert.Null(actual.ResponseBody);
    }

    private static ApiLogEntry Entry(int sequence) => new(
        DateTimeOffset.UtcNow,
        LogLevel.Warning,
        RequestId: string.Empty,
        SourceContext: "Test",
        RequestPath: "/test",
        Message: $"m-{sequence}",
        Exception: null,
        ElapsedMs: 0);
}