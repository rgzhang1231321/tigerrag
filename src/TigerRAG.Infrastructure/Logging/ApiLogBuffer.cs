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
            batch = new List<ApiLogEntry>(Pending);
            Pending.Clear();
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
                    entry.ElapsedMs
                }, transaction);
            }
            transaction.Commit();
            return batch.Count;
        }
        catch
        {
            // 日志组件自身故障不能拖垮请求处理；丢弃条目避免重试无限堆积。
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
        INSERT INTO api_log (timestamp, level, request_id, source_context, request_path, message, exception, elapsed_ms)
        VALUES (@Timestamp, @Level, @RequestId, @SourceContext, @RequestPath, @Message, @Exception, @ElapsedMs)
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