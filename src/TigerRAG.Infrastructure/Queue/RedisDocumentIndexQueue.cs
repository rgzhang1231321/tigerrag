using Microsoft.Extensions.Options;
using StackExchange.Redis;
using TigerRAG.Application.Documents;

namespace TigerRAG.Infrastructure.Queue;

/// <summary>
/// 基于 Redis List 的文档索引任务队列。键名由 <see cref="DocumentIndexQueueOptions.RedisKey"/> 控制。
/// 上传方 RPUSH 入队，Worker LPOP 出队（FIFO）。
/// StackExchange.Redis 3.x 没有原生 BLPOP 单 key 重载，采用 500ms 短轮询折中。
/// </summary>
public sealed class RedisDocumentIndexQueue(
    IConnectionMultiplexer multiplexer,
    IOptions<DocumentIndexQueueOptions> options) : IDocumentIndexQueue
{
    private readonly string _queueKey = options.Value.RedisKey;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    public async Task EnqueueAsync(Guid documentId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = multiplexer.GetDatabase();
        await db.ListRightPushAsync(_queueKey, documentId.ToString());
    }

    public async Task<Guid?> DequeueAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = multiplexer.GetDatabase();
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (true)
        {
            // 非阻塞 LPOP：栈顶为空返回 IsNullOrEmpty 的 RedisValue。
            RedisValue result;
            try
            {
                result = await db.ListLeftPopAsync(_queueKey);
            }
            catch (RedisConnectionException)
            {
                return null;
            }
            catch (RedisException)
            {
                return null;
            }

            if (!result.IsNullOrEmpty)
            {
                return Guid.TryParse(result.ToString(), out var id) ? id : null;
            }

            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                return null;
            }

            var sleep = remaining < PollInterval ? remaining : PollInterval;
            try
            {
                await Task.Delay(sleep, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }
    }
}
