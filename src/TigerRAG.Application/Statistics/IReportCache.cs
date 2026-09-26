namespace TigerRAG.Application.Statistics;

/// <summary>
/// 报表级缓存端口：为统计报表提供读/写/失效能力。
/// 实现负责序列化与传输层异常处理——调用方永不 catch 实现异常：
/// miss 或不可用均返回 null；写入失败由实现内部记录日志，对调用方透明。
/// </summary>
public interface IReportCache
{
    /// <summary>按 key 读取并反序列化；miss、TTL 过期、或传输层异常均返回 null。</summary>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class;

    /// <summary>写入并设置 TTL；传输层异常由实现记录日志并吞掉，不向上抛。</summary>
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken) where T : class;
}
