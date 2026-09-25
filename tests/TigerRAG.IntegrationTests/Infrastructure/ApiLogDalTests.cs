using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TigerRAG.Application.ApiLogs;
using TigerRAG.Infrastructure.ApiLogs.Dal;
using TigerRAG.Infrastructure.Persistence;
using TigerRAG.Infrastructure.Persistence.Entities.ApiLogs;

namespace TigerRAG.IntegrationTests.Infrastructure;

/// <summary>
/// 验证合并后 ApiLogDal 的单表查询语义：kind 判别隔离两类行、
/// 访问筛选（用户名/路径关键词命中合并路径或 action/状态码/时间窗）、
/// 消息行 level/keyword 既有语义不受影响、requestId 跨 kind 关联、分页。
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class ApiLogDalTests : IAsyncLifetime
{
    private const string TestDatabaseName = "tigerrag_api_log_test";
    private static readonly string AdminConnectionString =
        "Host=localhost;Port=5432;Database=postgres;Username=tigerrag;Password=And@2088;Timeout=5";
    private static readonly string TestConnectionString =
        $"Host=localhost;Port=5432;Database={TestDatabaseName};Username=tigerrag;Password=And@2088;Timeout=5";

    private ServiceProvider _rootProvider = null!;

    public async Task InitializeAsync()
    {
        if (!await LocalPostgresFixture.IsAvailableAsync())
            throw new InvalidOperationException("Local Postgres is not reachable.");

        NpgsqlConnection.ClearAllPools();
        await DropTestDatabaseAsync();
        await CreateTestDatabaseAsync();
        _rootProvider = BuildServiceProvider(TestConnectionString);
        await EnsureSchemaAndSeedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_rootProvider is not null) await _rootProvider.DisposeAsync();
        // 释放连接池后才能删库，避免残留连接占用。
        NpgsqlConnection.ClearAllPools();
        await DropTestDatabaseAsync();
    }

    /// <summary>kind='access' 只返回访问行（排除消息行），按时间倒序，且 15 字段投影完整。</summary>
    [Fact]
    public async Task ListAsync_KindAccess_ExcludesMessageRows_AndProjectsAccessFields()
    {
        await using var scope = _rootProvider.CreateAsyncScope();
        var dal = scope.ServiceProvider.GetRequiredService<IApiLogDal>();

        var result = await dal.ListAsync(Request(kind: "access"), CancellationToken.None);

        Assert.Equal(4, result.Total);
        Assert.All(result.Entries, e => Assert.Equal("access", e.Kind));
        // 时间倒序：最新的 A4（T4）排第一。
        Assert.Equal("req-a4", result.Entries[0].RequestId);

        // A1 的 15 字段投影逐项核对（合并路径/用户/action/状态码/脱敏体/耗时）。
        var a1 = Assert.Single(result.Entries, e => e.RequestId == "req-a1");
        Assert.Equal("Information", a1.Level);
        Assert.Null(a1.SourceContext);
        Assert.Equal("POST /api/logs/list?pageSize=20", a1.RequestPath);
        Assert.Equal("POST /api/logs/list?pageSize=20 200 42ms", a1.Message);
        Assert.Null(a1.Exception);
        Assert.Equal(42, a1.ElapsedMs);
        Assert.Equal("bob", a1.UserName);
        Assert.Equal("ApiLogs.List", a1.Action);
        Assert.Equal(200, a1.StatusCode);
        Assert.Equal("""{"pageSize":20,"passwordHash":"***"}""", a1.RequestBody);
        Assert.Null(a1.ResponseBody);
    }

    /// <summary>用户名筛选大小写不敏感（ILike），且只在 kind='access' 行内命中。</summary>
    [Fact]
    public async Task ListAsync_UserNameFilter_IsCaseInsensitiveWithinAccessKind()
    {
        await using var scope = _rootProvider.CreateAsyncScope();
        var dal = scope.ServiceProvider.GetRequiredService<IApiLogDal>();

        var result = await dal.ListAsync(Request(kind: "access", userName: "BO"), CancellationToken.None);

        // bob 的 A1/A4 命中（大小写不敏感）；alice、匿名行与消息行排除。
        Assert.Equal(2, result.Total);
        Assert.All(result.Entries, e => Assert.Equal("bob", e.UserName));
    }

    /// <summary>路径关键词同时命中合并路径（"POST /api/x?y=1"）或 action（"Controller.Action"）。</summary>
    [Fact]
    public async Task ListAsync_PathKeyword_MatchesMergedPathOrAction()
    {
        await using var scope = _rootProvider.CreateAsyncScope();
        var dal = scope.ServiceProvider.GetRequiredService<IApiLogDal>();

        var byPath = await dal.ListAsync(Request(kind: "access", pathKeyword: "knowledge"), CancellationToken.None);
        Assert.Equal("req-a2", Assert.Single(byPath.Entries).RequestId);

        var byAction = await dal.ListAsync(Request(kind: "access", pathKeyword: "Kb.List"), CancellationToken.None);
        Assert.Equal("req-a2", Assert.Single(byAction.Entries).RequestId);

        // 合并路径包含方法与查询串：按方法 + 查询参数也能命中。
        var byQuery = await dal.ListAsync(Request(kind: "access", pathKeyword: "pageSize=20"), CancellationToken.None);
        Assert.Equal("req-a1", Assert.Single(byQuery.Entries).RequestId);
    }

    /// <summary>状态码精确匹配；From/To 时间窗闭区间过滤。</summary>
    [Fact]
    public async Task ListAsync_StatusCodeAndTimeWindow_Filters()
    {
        await using var scope = _rootProvider.CreateAsyncScope();
        var dal = scope.ServiceProvider.GetRequiredService<IApiLogDal>();

        var byStatus = await dal.ListAsync(Request(kind: "access", statusCode: 404), CancellationToken.None);
        Assert.Equal("req-a2", Assert.Single(byStatus.Entries).RequestId);

        // 时间窗 [T2, T3] 闭区间：只有 A2、A3 落入。
        var window = await dal.ListAsync(
            Request(kind: "access", from: SeedBase.AddMinutes(2), to: SeedBase.AddMinutes(3)),
            CancellationToken.None);
        Assert.Equal(2, window.Total);
        Assert.Contains(window.Entries, e => e.RequestId == "req-a2");
        Assert.Contains(window.Entries, e => e.RequestId == "req-a3");
    }

    /// <summary>kind='message' 排除访问行，level/keyword 既有语义保持不变。</summary>
    [Fact]
    public async Task ListAsync_KindMessage_ExcludesAccessRows_AndKeepsMessageSemantics()
    {
        await using var scope = _rootProvider.CreateAsyncScope();
        var dal = scope.ServiceProvider.GetRequiredService<IApiLogDal>();

        var byLevel = await dal.ListAsync(Request(kind: "message", level: "Error"), CancellationToken.None);
        var m1 = Assert.Single(byLevel.Entries);
        Assert.Equal("req-m1", m1.RequestId);
        Assert.Equal("message", m1.Kind);
        // 消息行的访问维度字段恒为空。
        Assert.Null(m1.UserName);
        Assert.Null(m1.Action);
        Assert.Null(m1.StatusCode);
        Assert.Null(m1.RequestBody);
        Assert.Null(m1.ResponseBody);
        Assert.NotNull(m1.Exception);

        var byKeyword = await dal.ListAsync(Request(kind: "message", keyword: "耗时"), CancellationToken.None);
        Assert.Equal("req-a1", Assert.Single(byKeyword.Entries).RequestId);
    }

    /// <summary>同 requestId 跨 kind 关联：一次查询同时取回访问行与消息行（单表自关联）。</summary>
    [Fact]
    public async Task ListAsync_RequestIdFilter_CorrelatesBothKinds()
    {
        await using var scope = _rootProvider.CreateAsyncScope();
        var dal = scope.ServiceProvider.GetRequiredService<IApiLogDal>();

        var result = await dal.ListAsync(Request(requestId: "req-a1"), CancellationToken.None);

        Assert.Equal(2, result.Total);
        Assert.Single(result.Entries, e => e.Kind == "access");
        Assert.Single(result.Entries, e => e.Kind == "message");
    }

    /// <summary>分页在 kind='access' 行集内按时间倒序翻页。</summary>
    [Fact]
    public async Task ListAsync_Pagination_AppliesToAccessKind()
    {
        await using var scope = _rootProvider.CreateAsyncScope();
        var dal = scope.ServiceProvider.GetRequiredService<IApiLogDal>();

        var page2 = await dal.ListAsync(Request(kind: "access", page: 2, pageSize: 2), CancellationToken.None);

        // 倒序为 T4,T3 | T2,T1：第 2 页是 A2、A1。
        Assert.Equal(4, page2.Total);
        Assert.Equal(2, page2.Entries.Count);
        Assert.Equal("req-a2", page2.Entries[0].RequestId);
        Assert.Equal("req-a1", page2.Entries[1].RequestId);
    }

    /// <summary>种子基准时间：T1..T6 = SeedBase + 1..6 分钟（倒序断言依赖此间隔）。</summary>
    private static readonly DateTimeOffset SeedBase =
        new(2026, 9, 25, 8, 0, 0, TimeSpan.Zero);

    /// <summary>构造查询请求，仅填需要覆盖的筛选字段。</summary>
    private static ApiLogQueryRequest Request(
        string? kind = null,
        string? level = null,
        string? requestId = null,
        string? keyword = null,
        string? userName = null,
        string? pathKeyword = null,
        int? statusCode = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int page = 1,
        int pageSize = 20) =>
        new(from, to, level, requestId, keyword, kind, userName, pathKeyword, statusCode, page, pageSize);

    /// <summary>构建只含 DbContext + ApiLogDal 的最小容器。</summary>
    private static ServiceProvider BuildServiceProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddEntityFrameworkNpgsql()
            .AddDbContext<TigerRagDbContext>(options =>
                options.UseNpgsql(connectionString));
        services.AddScoped<IApiLogDal, ApiLogDal>();
        return services.BuildServiceProvider();
    }

    /// <summary>建表并写入混种种子：4 条访问行（T1-T4）+ 2 条消息行（T5-T6，其一与 A1 同 requestId）。</summary>
    private async Task EnsureSchemaAndSeedAsync()
    {
        await using var scope = _rootProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TigerRagDbContext>();
        await db.Database.EnsureCreatedAsync();
        await db.ApiLogs.ExecuteDeleteAsync();

        db.ApiLogs.AddRange(
            AccessRow("req-a1", 1, "bob", "ApiLogs.List", 200, 42,
                "POST /api/logs/list?pageSize=20",
                """{"pageSize":20,"passwordHash":"***"}""",
                responseBody: null),
            AccessRow("req-a2", 2, "alice", "Kb.List", 404, 15,
                "POST /api/knowledge-bases/list",
                """{"kbId":"kb-1"}""",
                responseBody: "[40400] 知识库不存在"),
            AccessRow("req-a3", 3, null, "Auth.Login", 200, 8,
                "POST /api/auth/login",
                """{"userName":"admin","passwordHash":"***"}""",
                responseBody: null),
            // 状态 200 但带失败响应体：对应前端 "200 失败" 的数据形态。
            AccessRow("req-a4", 4, "bob", "Documents.Upload", 200, 120,
                "POST /api/documents/upload",
                "[multipart/form-data] content-length=1024",
                responseBody: "[40000] 上传内容为空"),
            MessageRow("req-m1", 5, "Error",
                "TigerRAG.Api.Middleware.AccessLogMiddleware",
                "GET /api/statistics/reports",
                "请求处理失败",
                "System.InvalidOperationException: boom"),
            // 与 A1 同 requestId 的消息行：验证单表跨 kind 关联。
            MessageRow("req-a1", 6, "Warning",
                "TigerRAG.Api.Controllers.ApiLogsController",
                "GET /api/logs/list",
                "查询耗时偏高",
                exception: null));

        await db.SaveChangesAsync();
    }

    /// <summary>构造一条访问行种子（message 由路径+状态+耗时拼出，与中间件摘要格式一致）。</summary>
    private static api_log_record AccessRow(
        string requestId,
        int minuteOffset,
        string? userName,
        string action,
        int statusCode,
        int elapsedMs,
        string requestPath,
        string requestBody,
        string? responseBody) =>
        new()
        {
            Timestamp = SeedBase.AddMinutes(minuteOffset),
            Level = "Information",
            RequestId = requestId,
            SourceContext = null,
            RequestPath = requestPath,
            Message = $"{requestPath} {statusCode} {elapsedMs}ms",
            Exception = null,
            ElapsedMs = elapsedMs,
            Kind = "access",
            UserName = userName,
            Action = action,
            StatusCode = statusCode,
            RequestBody = requestBody,
            ResponseBody = responseBody,
        };

    /// <summary>构造一条消息行种子（访问维度字段全 NULL）。</summary>
    private static api_log_record MessageRow(
        string requestId,
        int minuteOffset,
        string level,
        string sourceContext,
        string requestPath,
        string message,
        string? exception) =>
        new()
        {
            Timestamp = SeedBase.AddMinutes(minuteOffset),
            Level = level,
            RequestId = requestId,
            SourceContext = sourceContext,
            RequestPath = requestPath,
            Message = message,
            Exception = exception,
            ElapsedMs = 0,
            Kind = "message",
        };

    /// <summary>创建专用测试库（已存在则忽略）。</summary>
    private static async Task CreateTestDatabaseAsync()
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand($"CREATE DATABASE {TestDatabaseName} TEMPLATE template0", connection);
        try { await cmd.ExecuteNonQueryAsync(); }
        catch (PostgresException ex) when (ex.SqlState == "42P04") { /* 已存在则忽略 */ }
    }

    /// <summary>删除专用测试库（先断开残留连接）。</summary>
    private static async Task DropTestDatabaseAsync()
    {
        try
        {
            await using var connection = new NpgsqlConnection(AdminConnectionString);
            await connection.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{TestDatabaseName}';" +
                $"DROP DATABASE IF EXISTS {TestDatabaseName};", connection);
            await cmd.ExecuteNonQueryAsync();
        }
        catch { /* 数据库可能不存在 */ }
    }
}
