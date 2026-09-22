using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TigerRAG.Application.Auth;
using TigerRAG.Application.Shared;
using TigerRAG.Infrastructure.Auth;
using TigerRAG.Infrastructure.Persistence;

namespace TigerRAG.IntegrationTests.Infrastructure;

/// <summary>角色-Endpoint 授权 DAL 行为：授权/撤销/批量/查询。需要在本地 Postgres 跑。</summary>
[Collection(nameof(PostgresCollection))]
public sealed class RoleEndpointGrantStoreTests : IAsyncLifetime
{
    private const string TestDatabaseName = "tigerrag_grant_test";
    private static readonly string AdminConnectionString =
        "Host=localhost;Port=5432;Database=postgres;Username=tigerrag;Password=And@2088;Timeout=5";
    private static readonly string TestConnectionString =
        $"Host=localhost;Port=5432;Database={TestDatabaseName};Username=tigerrag;Password=And@2088;Timeout=5";

    private ServiceProvider _rootProvider = null!;
    private IRoleEndpointGrantStore _store = null!;

    private static readonly MenuEndpointDescriptor[] SampleEndpoints =
    [
        new("documents", "documents.list", "列出文档", "POST", "list"),
        new("documents", "documents.upload", "上传文档", "POST", "upload"),
        new("documents", "documents.delete", "删除文档", "POST", "{id}/delete"),
    ];

    public async Task InitializeAsync()
    {
        if (!await LocalPostgresFixture.IsAvailableAsync())
            throw new InvalidOperationException("Local Postgres is not reachable.");

        Npgsql.NpgsqlConnection.ClearAllPools();
        await DropTestDatabaseAsync();
        await CreateTestDatabaseAsync();
        _rootProvider = BuildServiceProvider(TestConnectionString);
        await EnsureSchemaAsync(_rootProvider);
        _store = _rootProvider.GetRequiredService<IRoleEndpointGrantStore>();

        // 清理上一次测试残留的授权数据（测试库跨运行保留）
        await using var cleanupScope = _rootProvider.CreateAsyncScope();
        var cleanupDb = cleanupScope.ServiceProvider.GetRequiredService<TigerRagDbContext>();
        cleanupDb.RoleEndpointGrants.RemoveRange(cleanupDb.RoleEndpointGrants);
        await cleanupDb.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (_rootProvider is not null) await _rootProvider.DisposeAsync();
        await DropTestDatabaseAsync();
    }

    [Fact]
    public async Task GrantAsync_CreatesGrant()
    {
        await _store.GrantAsync("Viewer", "documents", "documents.list", Guid.NewGuid(), CancellationToken.None);

        var grants = await _store.ListByRoleAsync("Viewer", CancellationToken.None);
        Assert.Single(grants);
        Assert.Equal("documents.list", grants[0].EndpointKey);
    }

    [Fact]
    public async Task GrantAsync_Duplicate_IsIdempotent()
    {
        var actorId = Guid.NewGuid();
        await _store.GrantAsync("Viewer", "documents", "documents.list", actorId, CancellationToken.None);
        await _store.GrantAsync("Viewer", "documents", "documents.list", actorId, CancellationToken.None);

        var grants = await _store.ListByRoleAsync("Viewer", CancellationToken.None);
        Assert.Single(grants);
    }

    [Fact]
    public async Task HasGrantAsync_WithGrant_ReturnsTrue()
    {
        await _store.GrantAsync("Viewer", "documents", "documents.list", Guid.NewGuid(), CancellationToken.None);

        Assert.True(await _store.HasGrantAsync(["Viewer"], "documents.list", CancellationToken.None));
    }

    [Fact]
    public async Task HasGrantAsync_WithoutGrant_ReturnsFalse()
    {
        Assert.False(await _store.HasGrantAsync(["Viewer"], "documents.list", CancellationToken.None));
    }

    [Fact]
    public async Task RevokeAsync_RemovesGrant()
    {
        await _store.GrantAsync("Viewer", "documents", "documents.list", Guid.NewGuid(), CancellationToken.None);
        await _store.RevokeAsync("Viewer", "documents.list", CancellationToken.None);

        Assert.False(await _store.HasGrantAsync(["Viewer"], "documents.list", CancellationToken.None));
    }

    [Fact]
    public async Task RevokeAsync_NonExistent_IsSilent()
    {
        await _store.RevokeAsync("Viewer", "documents.list", CancellationToken.None);
        // 不抛异常
    }

    [Fact]
    public async Task GrantAllInMenuAsync_GrantsAllEndpoints()
    {
        var count = await _store.GrantAllInMenuAsync("Viewer", "documents", SampleEndpoints, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(3, count);
        var grants = await _store.ListByRoleAsync("Viewer", CancellationToken.None);
        Assert.Equal(3, grants.Count);
    }

    [Fact]
    public async Task RevokeAllInMenuAsync_RevokesAllEndpointsInMenu()
    {
        await _store.GrantAllInMenuAsync("Viewer", "documents", SampleEndpoints, Guid.NewGuid(), CancellationToken.None);
        var count = await _store.RevokeAllInMenuAsync("Viewer", "documents", CancellationToken.None);

        Assert.Equal(3, count);
        var grants = await _store.ListByRoleAsync("Viewer", CancellationToken.None);
        Assert.Empty(grants);
    }

    [Fact]
    public async Task HasGrantAsync_MatchesAnyUserRole()
    {
        await _store.GrantAsync("Editor", "documents", "documents.list", Guid.NewGuid(), CancellationToken.None);

        Assert.True(await _store.HasGrantAsync(["Viewer", "Editor"], "documents.list", CancellationToken.None));
        Assert.False(await _store.HasGrantAsync(["Viewer"], "documents.list", CancellationToken.None));
    }

    private static ServiceProvider BuildServiceProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddEntityFrameworkNpgsql()
            .AddDbContext<TigerRagDbContext>(options =>
                options.UseNpgsql(connectionString));
        services.AddScoped<IRoleEndpointGrantStore, RoleEndpointGrantStore>();
        return services.BuildServiceProvider();
    }

    private static async Task EnsureSchemaAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TigerRagDbContext>();
        await db.Database.EnsureCreatedAsync();
        // 仅创建 grant 表（其它表由 EnsureCreatedAsync 自动建）
    }

    private static async Task CreateTestDatabaseAsync()
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand($"CREATE DATABASE {TestDatabaseName} TEMPLATE template0", connection);
        try { await cmd.ExecuteNonQueryAsync(); }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "42P04") { /* 已存在则忽略 */ }
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