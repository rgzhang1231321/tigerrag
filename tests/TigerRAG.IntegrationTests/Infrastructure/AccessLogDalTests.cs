using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TigerRAG.Application.ApiLogs;
using TigerRAG.Infrastructure.ApiLogs.Dal;
using TigerRAG.Infrastructure.Persistence;
using TigerRAG.Infrastructure.Persistence.Entities.ApiLogs;

namespace TigerRAG.IntegrationTests.Infrastructure;

/// <summary>
/// 验证 AccessLogDal 的过滤、排序、分页与投影：
/// 时间范围 / 用户名 ILike / 路径关键词（path 或 action）/ 状态码精确 / RequestId，
/// 结果按时间倒序分页返回，投影携带全部访问日志字段。
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class AccessLogDalTests : IAsyncLifetime
{
    private const string TestDatabaseName = "tigerrag_access_log_test";
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
        await EnsureSchemaAsync(_rootProvider);
        await SeedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_rootProvider is not null) await _rootProvider.DisposeAsync();
        // 先清 ADO.NET 连接池再删库，否则池中的空闲连接会让 DROP DATABASE 失败。
        NpgsqlConnection.ClearAllPools();
        await DropTestDatabaseAsync();
    }

    [Fact]
    public async Task ListAsync_NoFilters_ReturnsAllOrderedByTimestampDesc()
    {
        await using var scope = _rootProvider.CreateAsyncScope();
        var dal = scope.ServiceProvider.GetRequiredService<IAccessLogDal>();

        var result = await dal.ListAsync(new AccessLogQueryRequest(null, null, null, null, null, null, 1, 20), CancellationToken.None);

        Assert.Equal(3, result.Total);
        // 时间倒序：最新（401 请求）在前。
        Assert.Equal(401, result.Entries[0].StatusCode);
        Assert.Equal(200, result.Entries[1].StatusCode);
        Assert.Equal(404, result.Entries[2].StatusCode);
    }

    [Fact]
    public async Task ListAsync_TimeRange_FiltersByFromAndTo()
    {
        await using var scope = _rootProvider.CreateAsyncScope();
        var dal = scope.ServiceProvider.GetRequiredService<IAccessLogDal>();

        // 只取 [T+1, ∞)（含边界）：剩下 200（T+1）与 401（T+3）两条，404（T+0）被排除。
        var result = await dal.ListAsync(
            new AccessLogQueryRequest(SeedBase.AddMinutes(1), null, null, null, null, null, 1, 20),
            CancellationToken.None);

        Assert.Equal(2, result.Total);
        Assert.All(result.Entries, e => Assert.True(e.Timestamp >= SeedBase.AddMinutes(1)));
    }

    [Fact]
    public async Task ListAsync_UserName_FiltersCaseInsensitively()
    {
        await using var scope = _rootProvider.CreateAsyncScope();
        var dal = scope.ServiceProvider.GetRequiredService<IAccessLogDal>();

        // ILike：大小写不敏感的部分匹配。
        var result = await dal.ListAsync(
            new AccessLogQueryRequest(null, null, "ALI", null, null, null, 1, 20),
            CancellationToken.None);

        Assert.Equal(1, result.Total);
        Assert.Equal("alice", result.Entries[0].UserName);
    }

    [Fact]
    public async Task ListAsync_PathKeyword_MatchesPathOrAction()
    {
        await using var scope = _rootProvider.CreateAsyncScope();
        var dal = scope.ServiceProvider.GetRequiredService<IAccessLogDal>();

        // 关键词命中 request_path。
        var byPath = await dal.ListAsync(
            new AccessLogQueryRequest(null, null, null, "no-such", null, null, 1, 20),
            CancellationToken.None);
        Assert.Equal(1, byPath.Total);
        Assert.Equal(404, byPath.Entries[0].StatusCode);

        // 关键词命中 action。
        var byAction = await dal.ListAsync(
            new AccessLogQueryRequest(null, null, null, "kb.list", null, null, 1, 20),
            CancellationToken.None);
        Assert.Equal(1, byAction.Total);
        Assert.Equal("Kb.List", byAction.Entries[0].Action);
    }

    [Fact]
    public async Task ListAsync_StatusCode_FiltersExactly()
    {
        await using var scope = _rootProvider.CreateAsyncScope();
        var dal = scope.ServiceProvider.GetRequiredService<IAccessLogDal>();

        var result = await dal.ListAsync(
            new AccessLogQueryRequest(null, null, null, null, 401, null, 1, 20),
            CancellationToken.None);

        Assert.Equal(1, result.Total);
        Assert.Equal(401, result.Entries[0].StatusCode);
        Assert.Equal("[40100] 未授权访问", result.Entries[0].ResponseBody);
    }

    [Fact]
    public async Task ListAsync_RequestId_FiltersExactly()
    {
        await using var scope = _rootProvider.CreateAsyncScope();
        var dal = scope.ServiceProvider.GetRequiredService<IAccessLogDal>();

        var result = await dal.ListAsync(
            new AccessLogQueryRequest(null, null, null, null, null, "req-200", 1, 20),
            CancellationToken.None);

        Assert.Equal(1, result.Total);
        Assert.Equal("req-200", result.Entries[0].RequestId);
    }

    [Fact]
    public async Task ListAsync_Pagination_SkipsAndTakes()
    {
        await using var scope = _rootProvider.CreateAsyncScope();
        var dal = scope.ServiceProvider.GetRequiredService<IAccessLogDal>();

        // 第 2 页、每页 2 条：只剩最旧的一条（404）。
        var result = await dal.ListAsync(
            new AccessLogQueryRequest(null, null, null, null, null, null, 2, 2),
            CancellationToken.None);

        Assert.Equal(3, result.Total);
        Assert.Single(result.Entries);
        Assert.Equal(404, result.Entries[0].StatusCode);
    }

    [Fact]
    public async Task ListAsync_Projection_CarriesAllFields()
    {
        await using var scope = _rootProvider.CreateAsyncScope();
        var dal = scope.ServiceProvider.GetRequiredService<IAccessLogDal>();

        var result = await dal.ListAsync(
            new AccessLogQueryRequest(null, null, null, null, null, "req-401", 1, 20),
            CancellationToken.None);

        var entry = Assert.Single(result.Entries);
        Assert.True(entry.Id > 0);
        Assert.Equal("req-401", entry.RequestId);
        Assert.Equal(SeedUserId, entry.UserId);
        Assert.Equal("bob", entry.UserName);
        Assert.Equal("POST", entry.HttpMethod);
        Assert.Equal("/api/knowledge-bases/list", entry.RequestPath);
        Assert.Equal("?page=1", entry.QueryString);
        Assert.Equal("Kb.List", entry.Action);
        Assert.Contains("***", entry.RequestBody);
        Assert.Equal("[40100] 未授权访问", entry.ResponseBody);
        Assert.Equal(401, entry.StatusCode);
        Assert.True(entry.ElapsedMs >= 0);
        Assert.Equal("198.51.100.9", entry.Ip);
    }

    private static readonly Guid SeedUserId = Guid.NewGuid();

    /// <summary>种子时间基准：三条日志按分钟递增。</summary>
    private static readonly DateTimeOffset SeedBase =
        new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>种入三条覆盖不同形态的访问日志：成功 200 / 失败 401 / 匿名 404。</summary>
    private async Task SeedAsync()
    {
        await using var scope = _rootProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TigerRagDbContext>();
        // DROP DATABASE 与 pg_terminate_backend 存在竞态，失败时旧库会残留旧数据；
        // 这里先清表保证每个用例都从恰好 3 行种子开始，与删库成败解耦。
        await db.AccessLogs.ExecuteDeleteAsync();
        db.AccessLogs.AddRange(
            new api_access_log_record
            {
                Timestamp = SeedBase.AddMinutes(1),
                RequestId = "req-200",
                UserId = Guid.NewGuid(),
                UserName = "alice",
                HttpMethod = "POST",
                RequestPath = "/api/users/list",
                QueryString = null,
                Action = "Users.List",
                RequestBody = """{"page":1,"pageSize":20}""",
                ResponseBody = null,
                StatusCode = 200,
                ElapsedMs = 35,
                Ip = "203.0.113.7"
            },
            new api_access_log_record
            {
                Timestamp = SeedBase.AddMinutes(3),
                RequestId = "req-401",
                UserId = SeedUserId,
                UserName = "bob",
                HttpMethod = "POST",
                RequestPath = "/api/knowledge-bases/list",
                QueryString = "?page=1",
                Action = "Kb.List",
                RequestBody = """{"userName":"bob","passwordHash":"***"}""",
                ResponseBody = "[40100] 未授权访问",
                StatusCode = 401,
                ElapsedMs = 12,
                Ip = "198.51.100.9"
            },
            new api_access_log_record
            {
                Timestamp = SeedBase,
                RequestId = "req-404",
                UserId = null,
                UserName = null,
                HttpMethod = "POST",
                RequestPath = "/api/no-such",
                QueryString = null,
                Action = null,
                RequestBody = null,
                ResponseBody = "[40400] 资源不存在",
                StatusCode = 404,
                ElapsedMs = 2,
                Ip = null
            });
        await db.SaveChangesAsync();
    }

    private static ServiceProvider BuildServiceProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddEntityFrameworkNpgsql()
            .AddDbContext<TigerRagDbContext>(options =>
                options.UseNpgsql(connectionString));
        services.AddScoped<TigerRagDbContext>();
        services.AddScoped<IAccessLogDal, AccessLogDal>();
        return services.BuildServiceProvider();
    }

    private static async Task EnsureSchemaAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TigerRagDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    private static async Task CreateTestDatabaseAsync()
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand($"CREATE DATABASE {TestDatabaseName} TEMPLATE template0", connection);
        try { await cmd.ExecuteNonQueryAsync(); }
        catch (PostgresException ex) when (ex.SqlState == "42P04") { /* 已存在则忽略 */ }
    }

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
