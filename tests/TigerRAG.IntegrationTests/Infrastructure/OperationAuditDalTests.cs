using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TigerRAG.Application.OperationAudit;
using TigerRAG.Application.Shared;
using TigerRAG.Application.Users;
using TigerRAG.Infrastructure.Dal;
using TigerRAG.Infrastructure.OperationAudit.Dal;
using TigerRAG.Infrastructure.Persistence;

namespace TigerRAG.IntegrationTests.Infrastructure;

/// <summary>
/// 验证 OperationAuditDal.RecordAsync 在 UoW 内/外的行为：
/// - UoW 内调用时审计与业务同事务（回滚时审计也回滚）
/// - UoW 外调用时审计独立提交。
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class OperationAuditDalTests : IAsyncLifetime
{
    private const string TestDatabaseName = "tigerrag_audit_tx_test";
    private static readonly string AdminConnectionString =
        "Host=localhost;Port=5432;Database=postgres;Username=tigerrag;Password=And@2088;Timeout=5";
    private static readonly string TestConnectionString =
        $"Host=localhost;Port=5432;Database={TestDatabaseName};Username=tigerrag;Password=And@2088;Timeout=5";

    private ServiceProvider _rootProvider = null!;

    public async Task InitializeAsync()
    {
        if (!await LocalPostgresFixture.IsAvailableAsync())
            throw new InvalidOperationException("Local Postgres is not reachable.");

        Npgsql.NpgsqlConnection.ClearAllPools();
        await DropTestDatabaseAsync();
        await CreateTestDatabaseAsync();
        _rootProvider = BuildServiceProvider(TestConnectionString);
        await EnsureSchemaAsync(_rootProvider);
    }

    public async Task DisposeAsync()
    {
        if (_rootProvider is not null) await _rootProvider.DisposeAsync();
        await DropTestDatabaseAsync();
    }

    /// <summary>RecordAsync 在 UoW 事务内调用：注入异常触发回滚，审计记录不应落库。</summary>
    [Fact]
    public async Task RecordAsync_InsideUnitOfWork_ParticipatesInTransaction()
    {
        await using var scope = _rootProvider.CreateAsyncScope();
        var audit = scope.ServiceProvider.GetRequiredService<IOperationAuditWriter>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var db = scope.ServiceProvider.GetRequiredService<TigerRagDbContext>();

        var beforeCount = await db.OperationAudits.CountAsync();

        try
        {
            await uow.ExecuteAsync(async ct =>
            {
                await audit.RecordAsync(new OperationAuditEntry(
                    Guid.Empty, "system", "test.action", "test", "1", "should rollback"), ct);
                // 触发回滚
                throw new InvalidOperationException("force rollback");
            }, CancellationToken.None);
        }
        catch (InvalidOperationException) { /* 预期内的回滚异常 */ }

        var afterCount = await db.OperationAudits.CountAsync();
        Assert.Equal(beforeCount, afterCount);  // 审计与业务一起回滚
    }

    /// <summary>RecordAsync 在 UoW 事务外调用：独立提交，不受外围异常影响。</summary>
    [Fact]
    public async Task RecordAsync_OutsideUnitOfWork_CommitsIndependently()
    {
        await using var scope = _rootProvider.CreateAsyncScope();
        var audit = scope.ServiceProvider.GetRequiredService<IOperationAuditWriter>();
        var db = scope.ServiceProvider.GetRequiredService<TigerRagDbContext>();

        var beforeCount = await db.OperationAudits.CountAsync();

        await audit.RecordAsync(new OperationAuditEntry(
            Guid.Empty, "system", "test.action", "test", "2", "independent commit"),
            CancellationToken.None);

        var afterCount = await db.OperationAudits.CountAsync();
        Assert.Equal(beforeCount + 1, afterCount);  // 独立提交成功
    }

    private static ServiceProvider BuildServiceProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddEntityFrameworkNpgsql()
            .AddDbContext<TigerRagDbContext>(options =>
                options.UseNpgsql(connectionString));
        services.AddScoped<TigerRagDbContext>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IOperationAuditWriter, OperationAuditDal>();
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
