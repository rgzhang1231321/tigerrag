using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace TigerRAG.Infrastructure.Statistics;

/// <summary>
/// 基于 Redis String 的报表缓存实现。键名由调用方控制；值使用 System.Text.Json 序列化。
/// 所有传输层异常（连接断开、超时）在本类内部记录后吞掉，对调用方表现为"缓存 miss/写入忽略"。
/// </summary>
public sealed class RedisReportCache : Application.Statistics.IReportCache
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IConnectionMultiplexer _multiplexer;
    private readonly ILogger<RedisReportCache> _logger;

    public RedisReportCache(IConnectionMultiplexer multiplexer, ILogger<RedisReportCache> logger)
    {
        _multiplexer = multiplexer;
        _logger = logger;
    }

    /// <summary>读取 Redis String 并反序列化；miss、TTL 过期、或传输层异常均返回 null。</summary>
    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var db = _multiplexer.GetDatabase();
            var value = await db.StringGetAsync(key);
            if (value.IsNullOrEmpty) return null;
            return JsonSerializer.Deserialize<T>((string)value!, SerializerOptions);
        }
        catch (RedisConnectionException error)
        {
            _logger.LogWarning(error, "报表缓存读取失败；按 miss 处理。key={Key}", key);
            return null;
        }
        catch (RedisException error)
        {
            _logger.LogWarning(error, "报表缓存读取异常；按 miss 处理。key={Key}", key);
            return null;
        }
    }

    /// <summary>序列化并写入 Redis String，设置 TTL；传输层异常记录后吞掉。</summary>
    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken) where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var db = _multiplexer.GetDatabase();
            var json = JsonSerializer.Serialize(value, SerializerOptions);
            await db.StringSetAsync(key, json, ttl);
        }
        catch (RedisConnectionException error)
        {
            _logger.LogWarning(error, "报表缓存写入失败；本次跳过。key={Key}", key);
        }
        catch (RedisException error)
        {
            _logger.LogWarning(error, "报表缓存写入异常；本次跳过。key={Key}", key);
        }
    }
}
