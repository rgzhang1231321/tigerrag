using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace TigerRAG.Infrastructure.Logging;

/// <summary>
/// 访问日志缓冲：AccessLogMiddleware 以无 DI 方式入队，后台 <see cref="AccessLogFlusherService"/>
/// 按容量或定时批量 INSERT 到 <c>api_access_log</c>。写入采用参数化 Dapper 单条 INSERT，
/// DB 异常静默吞掉以避免日志组件拖垮请求主流程。与 ApiLogBuffer 相互独立，互不影响。
/// </summary>
public static class AccessLogBuffer
{
    private static readonly object Gate = new();
    private static readonly List<AccessLogEntry> Pending = new();
    private static AccessLogConfiguration? _configuration;
    private static IConfiguration? _appConfiguration;
    private static long _failureCount;

    /// <summary>DB 写入失败累计计数；观测用，不影响主流程。线程安全。</summary>
    public static long FailureCount => Interlocked.Read(ref _failureCount);

    /// <summary>由 DI 容器在启动期一次性调用，绑定配置与根配置（用于解析连接串）。</summary>
    public static void Configure(AccessLogConfiguration configuration, IConfiguration appConfiguration)
    {
        lock (Gate)
        {
            _configuration = configuration;
            _appConfiguration = appConfiguration;
        }
    }

    /// <summary>入队一条访问日志；未配置时静默丢弃，超过容量时丢最旧。</summary>
    public static void Enqueue(AccessLogEntry entry)
    {
        if (_configuration is null)
        {
            return;
        }

        lock (Gate)
        {
            // 容量上限：超过时丢最旧，保证内存有界；保留最新窗口便于排障。
            var capacity = _configuration.Capacity;
            if (capacity > 0 && Pending.Count >= capacity)
            {
                Pending.RemoveAt(0);
            }

            Pending.Add(entry);
        }
    }

    /// <summary>取出当前条目并尝试批量写入 DB；返回成功写入的条数。</summary>
    public static int TryFlush()
    {
        AccessLogConfiguration configuration;
        IConfiguration appConfiguration;
        List<AccessLogEntry> batch;
        lock (Gate)
        {
            if (_configuration is null || _appConfiguration is null || Pending.Count == 0)
            {
                return 0;
            }

            configuration = _configuration;
            appConfiguration = _appConfiguration;
            // 按 BatchSize 截断：单批 INSERT 大小有界；剩余留给下一轮。
            var take = Math.Min(configuration.BatchSize, Pending.Count);
            if (take <= 0)
            {
                return 0;
            }

            batch = Pending.GetRange(0, take);
        }

        try
        {
            using var connection = new NpgsqlConnection(ResolveConnectionString(appConfiguration, configuration));
            connection.Open();
            using var transaction = connection.BeginTransaction();
            foreach (var entry in batch)
            {
                connection.Execute(InsertSql, new
                {
                    entry.Timestamp,
                    entry.RequestId,
                    entry.UserId,
                    entry.UserName,
                    entry.HttpMethod,
                    entry.RequestPath,
                    entry.QueryString,
                    entry.Action,
                    entry.RequestBody,
                    entry.ResponseBody,
                    entry.StatusCode,
                    entry.ElapsedMs,
                    entry.Ip
                }, transaction);
            }

            transaction.Commit();

            // 写入成功的 batch 才从 Pending 摘除，失败则保留待下一轮重试。
            lock (Gate)
            {
                Pending.RemoveRange(0, batch.Count);
            }

            return batch.Count;
        }
        catch (Exception ex)
        {
            // 失败可观测：stderr 留痕 + 计数器自增；条目仍留缓冲里等下轮重试。
            Interlocked.Increment(ref _failureCount);
            Console.Error.WriteLine($"[AccessLogBuffer] flush failed: {ex.GetType().Name}: {ex.Message}");
            return 0;
        }
    }

    /// <summary>解析写入连接串：优先 ConnectionStringKey，回落 PostgreSql。</summary>
    private static string ResolveConnectionString(IConfiguration appConfiguration, AccessLogConfiguration configuration)
    {
        var key = string.IsNullOrWhiteSpace(configuration.ConnectionStringKey) ? "Logging" : configuration.ConnectionStringKey;
        return appConfiguration.GetConnectionString(key)
            ?? appConfiguration.GetConnectionString("PostgreSql")
            ?? throw new InvalidOperationException("AccessLogBuffer requires ConnectionStrings:Logging or ConnectionStrings:PostgreSql.");
    }

    private const string InsertSql = """
        INSERT INTO api_access_log (timestamp, request_id, user_id, user_name, http_method, request_path, query_string, action, request_body, response_body, status_code, elapsed_ms, ip)
        VALUES (@Timestamp, @RequestId, @UserId, @UserName, @HttpMethod, @RequestPath, @QueryString, @Action, @RequestBody, @ResponseBody, @StatusCode, @ElapsedMs, @Ip)
        """;

    /// <summary>仅测试使用：取出当前缓冲中的所有条目而不触发 DB 写入。</summary>
    internal static List<AccessLogEntry> DrainForTest()
    {
        lock (Gate)
        {
            var list = new List<AccessLogEntry>(Pending);
            Pending.Clear();
            return list;
        }
    }

    /// <summary>仅测试使用：清空配置以隔离测试间状态。</summary>
    internal static void ResetForTest()
    {
        lock (Gate)
        {
            _configuration = null;
            _appConfiguration = null;
            Pending.Clear();
        }
    }
}
