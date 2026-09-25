using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace TigerRAG.Infrastructure.Logging;

/// <summary>
/// 全局日志缓冲：日志器以无 DI 方式入队，后台 <see cref="ApiLogFlusherService"/> 按容量或定时批量 INSERT。
/// 写入采用参数化 Dapper 单条 INSERT，相比参考项目的字符串拼接规避 SQL 注入并支持 PostgreSQL 类型映射。
/// DB 异常静默吞掉以避免日志组件拖垮请求主流程。
/// </summary>
public static class ApiLogBuffer
{
    private static readonly object Gate = new();
    private static readonly List<ApiLogEntry> Pending = new();
    private static ApiLogConfiguration? _configuration;
    private static IConfiguration? _appConfiguration;
    private static long _failureCount;

    /// <summary>DB 写入失败累计计数；观测用，不影响主流程。线程安全。</summary>
    public static long FailureCount => Interlocked.Read(ref _failureCount);

    /// <summary>由 DI 容器在启动期一次性调用；不重复配置则跳过。</summary>
    public static void Configure(ApiLogConfiguration configuration, IConfiguration appConfiguration)
    {
        lock (Gate)
        {
            _configuration = configuration;
            _appConfiguration = appConfiguration;
        }
    }

    public static void Enqueue(ApiLogEntry entry)
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
        ApiLogConfiguration configuration;
        IConfiguration appConfiguration;
        List<ApiLogEntry> batch;
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

            // 整批在同一把锁内取出并摘除：多个 flusher（如集成测试的多工厂）并发刷写时，
            // 同一批不会被取走两次，杜绝重复写入与摘除越界。
            batch = Pending.GetRange(0, take);
            Pending.RemoveRange(0, take);
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
                    Level = entry.Level.ToString(),
                    entry.RequestId,
                    entry.SourceContext,
                    entry.RequestPath,
                    entry.Message,
                    entry.Exception,
                    entry.ElapsedMs,
                    entry.Kind,
                    entry.UserName,
                    entry.Action,
                    entry.StatusCode,
                    entry.RequestBody,
                    entry.ResponseBody
                }, transaction);
            }
            transaction.Commit();

            return batch.Count;
        }
        catch (Exception ex)
        {
            // 失败可观测：stderr 留痕 + 计数器自增。
            Interlocked.Increment(ref _failureCount);
            Console.Error.WriteLine($"[ApiLogBuffer] flush failed: {ex.GetType().Name}: {ex.Message}");
            // 失败整批插回队头等下一轮重试（保持时间顺序）；超容量时从队头丢弃最旧，保证内存有界。
            lock (Gate)
            {
                Pending.InsertRange(0, batch);
                var capacity = _configuration?.Capacity ?? 0;
                if (capacity > 0)
                {
                    while (Pending.Count > capacity)
                    {
                        Pending.RemoveAt(0);
                    }
                }
            }

            return 0;
        }
    }

    private static string ResolveConnectionString(IConfiguration appConfiguration, ApiLogConfiguration configuration)
    {
        var key = string.IsNullOrWhiteSpace(configuration.ConnectionStringKey) ? "Logging" : configuration.ConnectionStringKey;
        return appConfiguration.GetConnectionString(key)
            ?? appConfiguration.GetConnectionString("PostgreSql")
            ?? throw new InvalidOperationException("ApiLogBuffer requires ConnectionStrings:Logging or ConnectionStrings:PostgreSql.");
    }

    private const string InsertSql = """
        INSERT INTO api_log (timestamp, level, request_id, source_context, request_path, message, exception, elapsed_ms, kind, user_name, action, status_code, request_body, response_body)
        VALUES (@Timestamp, @Level, @RequestId, @SourceContext, @RequestPath, @Message, @Exception, @ElapsedMs, @Kind, @UserName, @Action, @StatusCode, @RequestBody, @ResponseBody)
        """;

    /// <summary>仅测试使用：取出当前缓冲中的所有条目而不触发 DB 写入。</summary>
    internal static List<ApiLogEntry> DrainForTest()
    {
        lock (Gate)
        {
            var list = new List<ApiLogEntry>(Pending);
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
