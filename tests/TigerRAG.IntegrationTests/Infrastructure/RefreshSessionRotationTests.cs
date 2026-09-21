using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TigerRAG.Application.Auth;
using TigerRAG.Application.Users;
using TigerRAG.Infrastructure.Auth.Dal;
using TigerRAG.Infrastructure.Dal;
using TigerRAG.Infrastructure.Identity;
using TigerRAG.Infrastructure.Persistence;
using TigerRAG.Infrastructure.Persistence.Entities.Auth;

namespace TigerRAG.IntegrationTests.Infrastructure;

/// <summary>
/// 验证 <see cref="RefreshSessionDal.RotateAsync"/> 的行级锁语义。
/// 两个并发请求使用同一个旧令牌，恰好一个成功签发新会话，另一个被数据库锁阻挡后看到原行已撤销。
/// 必须在本地 Postgres 上跑（与 <c>LocalPostgresFixture</c> 一致）；无 DB 时 fail loud。
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class RefreshSessionRotationTests : IAsyncLifetime
{
    private const string TestDatabaseName = "tigerrag_rotation_test";
    private static readonly string AdminConnectionString =
        "Host=localhost;Port=5432;Database=postgres;Username=tigerrag;Password=And@2088;Timeout=5";
    private static readonly string TestConnectionString =
        $"Host=localhost;Port=5432;Database={TestDatabaseName};Username=tigerrag;Password=And@2088;Timeout=5";

    private ServiceProvider _rootProvider = null!;

    public async Task InitializeAsync()
    {
        if (!await LocalPostgresFixture.IsAvailableAsync())
        {
            throw new InvalidOperationException("Local Postgres is not reachable; RefreshSessionRotationTests requires it.");
        }

        await DropTestDatabaseAsync();
        await CreateTestDatabaseAsync();
        _rootProvider = BuildServiceProvider(TestConnectionString);
        await EnsureSchemaAsync(_rootProvider);
    }

    public async Task DisposeAsync()
    {
        if (_rootProvider is not null)
        {
            await _rootProvider.DisposeAsync();
        }

        await DropTestDatabaseAsync();
    }

    [Fact]
    public async Task RotateAsync_TwoConcurrentCallsOnSameToken_OnlyOneReturnsNewSession()
    {
        // 用一个稳定的 raw token 来构造 seed；hash 是 DAL 内部计算方式。
        var rawToken = "raw-refresh-token-under-test";
        var (userId, originalRecordId) = await SeedUserAndRefreshTokenAsync(rawToken);

        // 两个独立 scope，确保拿到各自的 DbContext 与事务。
        using var scope1 = _rootProvider.CreateScope();
        using var scope2 = _rootProvider.CreateScope();
        var dal1 = scope1.ServiceProvider.GetRequiredService<IRefreshSessionDal>();
        var dal2 = scope2.ServiceProvider.GetRequiredService<IRefreshSessionDal>();

        var task1 = Task.Run(() => dal1.RotateAsync(rawToken, CancellationToken.None));
        var task2 = Task.Run(() => dal2.RotateAsync(rawToken, CancellationToken.None));
        var results = await Task.WhenAll(task1, task2);

        var successCount = results.Count(result => result is not null);
        Assert.Equal(1, successCount);

        using var verifyScope = _rootProvider.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<TigerRagDbContext>();

        var original = await verifyContext.RefreshTokens
            .AsNoTracking()
            .SingleAsync(record => record.Id == originalRecordId);
        Assert.NotNull(original.RevokedAt);

        var allForUser = await verifyContext.RefreshTokens
            .AsNoTracking()
            .Where(record => record.UserId == userId)
            .OrderBy(record => record.CreatedAt)
            .ToListAsync();
        // seed + 1 winner 替换 = 2 行。
        Assert.Equal(2, allForUser.Count);
    }

    private static ServiceProvider BuildServiceProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddDbContext<TigerRagDbContext>(options => options.UseNpgsql(connectionString));
        services
            .AddIdentityCore<AppUser>(options =>
            {
                options.Password.RequiredLength = 5;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireDigit = false;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddSignInManager()
            .AddEntityFrameworkStores<TigerRagDbContext>();
        services.AddScoped<IRefreshSessionDal, RefreshSessionDal>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        return services.BuildServiceProvider();
    }

    private static async Task EnsureSchemaAsync(ServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TigerRagDbContext>();
        await context.Database.EnsureCreatedAsync();
    }

    private async Task<(Guid userId, Guid recordId)> SeedUserAndRefreshTokenAsync(string rawToken)
    {
        using var scope = _rootProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TigerRagDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = $"rotation-test-{Guid.NewGuid():N}",
            PasswordSalt = "salt",
            CreatedAt = DateTimeOffset.UtcNow
        };
        var createResult = await userManager.CreateAsync(user, "placeholder-password");
        if (!createResult.Succeeded)
        {
            throw new InvalidOperationException(
                "Seed user creation failed: " + string.Join("; ", createResult.Errors.Select(error => error.Description)));
        }

        // DAL 内部对 value 走 SHA-256；这里用同一算法算出 TokenHash 来构造等价 seed。
        var tokenHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(rawToken)));
        // stamp 必须与用户当前 stamp 一致，否则 RotateAsync 在新检查下会拒轮换。
        var stamp = await userManager.GetSecurityStampAsync(user);
        var record = new TigerRAG.Infrastructure.Persistence.Entities.Auth.refresh_token_record
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = tokenHash,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
            CreatedAt = DateTimeOffset.UtcNow,
            SecurityStamp = stamp
        };
        context.RefreshTokens.Add(record);
        await context.SaveChangesAsync();

        return (user.Id, record.Id);
    }

    private static async Task CreateTestDatabaseAsync()
    {
        await using var connection = new Npgsql.NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand(
            $"CREATE DATABASE \"{TestDatabaseName}\"", connection);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task DropTestDatabaseAsync()
    {
        // FORCE 断开已有连接，再 DROP；避免上一次跑挂残留的 session 阻塞。
        await using var connection = new Npgsql.NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await using (var terminate = new Npgsql.NpgsqlCommand(
            "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @name AND pid <> pg_backend_pid()",
            connection))
        {
            terminate.Parameters.AddWithValue("name", TestDatabaseName);
            await terminate.ExecuteNonQueryAsync();
        }
        await using var drop = new Npgsql.NpgsqlCommand(
            $"DROP DATABASE IF EXISTS \"{TestDatabaseName}\"", connection);
        await drop.ExecuteNonQueryAsync();
    }
}