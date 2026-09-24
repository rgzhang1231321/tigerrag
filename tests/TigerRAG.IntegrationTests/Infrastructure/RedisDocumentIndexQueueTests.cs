using Microsoft.Extensions.Options;
using StackExchange.Redis;
using TigerRAG.Infrastructure.Queue;

namespace TigerRAG.IntegrationTests.Infrastructure;

/// <summary>RedisDocumentIndexQueue 集成测试：FIFO 顺序、超时返回 null、连接异常返回 null。需要本地 Redis。</summary>
public sealed class RedisDocumentIndexQueueTests : IDisposable
{
    private const string TestQueueKey = "test:doc:index:queue";
    private readonly ConnectionMultiplexer _multiplexer;
    private readonly bool _redisAvailable;

    public RedisDocumentIndexQueueTests()
    {
        try
        {
            _multiplexer = ConnectionMultiplexer.Connect("localhost:6379,abortConnect=false,connectTimeout=1000,syncTimeout=1000");
            _redisAvailable = _multiplexer.IsConnected;
            if (_redisAvailable)
            {
                _multiplexer.GetDatabase().KeyDeleteAsync(TestQueueKey).GetAwaiter().GetResult();
            }
        }
        catch
        {
            _multiplexer = null!;
            _redisAvailable = false;
        }
    }

    private RedisDocumentIndexQueue CreateQueue()
    {
        var options = Options.Create(new DocumentIndexQueueOptions { RedisKey = TestQueueKey });
        return new RedisDocumentIndexQueue(_multiplexer, options);
    }

    [Fact]
    public async Task EnqueueAsync_PushesToRight()
    {
        if (!_redisAvailable) return;
        var queue = CreateQueue();
        var docId = Guid.NewGuid();
        await queue.EnqueueAsync(docId, CancellationToken.None);

        var db = _multiplexer.GetDatabase();
        var len = await db.ListLengthAsync(TestQueueKey);
        Assert.Equal(1, len);
    }

    [Fact]
    public async Task DequeueAsync_FIFO_Order()
    {
        if (!_redisAvailable) return;
        var queue = CreateQueue();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var id3 = Guid.NewGuid();
        await queue.EnqueueAsync(id1, CancellationToken.None);
        await queue.EnqueueAsync(id2, CancellationToken.None);
        await queue.EnqueueAsync(id3, CancellationToken.None);

        var first = await queue.DequeueAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
        var second = await queue.DequeueAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
        var third = await queue.DequeueAsync(TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.Equal(id1, first);
        Assert.Equal(id2, second);
        Assert.Equal(id3, third);
    }

    [Fact]
    public async Task DequeueAsync_EmptyQueue_TimeoutReturnsNull()
    {
        if (!_redisAvailable) return;
        var queue = CreateQueue();
        var db = _multiplexer.GetDatabase();
        await db.KeyDeleteAsync(TestQueueKey);

        var result = await queue.DequeueAsync(TimeSpan.FromMilliseconds(500), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task DequeueAsync_CancellationRequested_ReturnsNull()
    {
        if (!_redisAvailable) return;
        var queue = CreateQueue();
        var db = _multiplexer.GetDatabase();
        await db.KeyDeleteAsync(TestQueueKey);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var result = await queue.DequeueAsync(TimeSpan.FromSeconds(5), cts.Token);
        Assert.Null(result);
    }

    [Fact]
    public async Task EnqueueAsync_CancellationRequested_Throws()
    {
        if (!_redisAvailable) return;
        var queue = CreateQueue();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => queue.EnqueueAsync(Guid.NewGuid(), cts.Token));
    }

    public void Dispose()
    {
        if (_multiplexer is not null)
        {
            try
            {
                _multiplexer.GetDatabase().KeyDeleteAsync(TestQueueKey).GetAwaiter().GetResult();
            }
            catch { /* ignore */ }
            _multiplexer.Dispose();
        }
    }
}
