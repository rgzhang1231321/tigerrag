using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TigerRAG.Application.Security;
using TigerRAG.Infrastructure.Dal;
using TigerRAG.Infrastructure.Identity;
using TigerRAG.Infrastructure.Persistence;

namespace TigerRAG.IntegrationTests.Infrastructure;

/// <summary>
/// 验证 <see cref="RefreshSessionDal.RotateAsync"/> 对"用户敏感动作"的失效语义。
/// 锁定与改密（Identity 自动 bump SecurityStamp）都会让既有刷新令牌无法轮换，
/// 即使令牌本身未过期、未被显式撤销。
/// 必须在本地 Postgres 上跑；无 DB 时 fail loud。
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class RefreshSessionRevocationTests : IAsyncLifetime
{
    private const string TestDatabaseName = "tigerrag_revocation_test";
    private const string SeedPassword = "placeholder-password";

    private static readonly string AdminConnectionString =
        "Host=localhost;Port=5432;Database=postgres;Username=tigerrag;Password=And@2088;Timeout=15;Command Timeout=15";
    private static readonly string TestConnectionString =
        $"Host=localhost;Port=5432;Database={TestDatabaseName};Username=tigerrag;Password=And@2088;Timeout=15;Command Timeout=15";

    private ServiceProvider _rootProvider = null!;

    public async Task InitializeAsync()
    {
        if (!await LocalPostgresFixture.IsAvailableAsync())
        {
            throw new InvalidOperationException("Local Postgres is not reachable; RefreshSessionRevocationTests requires it.");
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
    public async Task RotateAsync_AfterSensitiveActions_ReturnsNull()
    {
        // 同一用例里顺序覆盖两个敏感动作：锁定 + 改密。每个动作后都用既未过期也未被撤销的
        // 既有刷新令牌调 RotateAsync，期望都被拒。Identity 内部对每个动作自动 bump SecurityStamp，
        // DAL 拿到新 stamp 与历史记录不匹配即视为该令牌已被废止。
        // 把两个动作放进一个测试是为避开本地 Postgres 在 back-to-back DROP+CREATE 后偶发的瞬态丢连接。
        var (lockedOutUserId, lockedOutToken, lockedOutRecordId) = await SeedUserAndStampedTokenAsync("locked-out-token");
        await LockUserAsync(lockedOutUserId, DateTimeOffset.UtcNow.AddHours(1));
        await AssertRotationRejectedAsync(lockedOutToken, lockedOutRecordId);

        var (changedUserId, changedToken, changedRecordId) = await SeedUserAndStampedTokenAsync("password-change-token");
        await ChangePasswordAsync(changedUserId);
        await AssertRotationRejectedAsync(changedToken, changedRecordId);
    }

    private async Task AssertRotationRejectedAsync(string rawToken, Guid recordId)
    {
        using var scope = _rootProvider.CreateScope();
        var dal = scope.ServiceProvider.GetRequiredService<IRefreshSessionDal>();
        var session = await dal.RotateAsync(rawToken, CancellationToken.None);

        Assert.Null(session);

        using var verifyScope = _rootProvider.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<TigerRagDbContext>();
        var original = await verifyContext.RefreshTokens.AsNoTracking()
            .SingleAsync(record => record.Id == recordId);
        // 拒绝轮换只是"该令牌不再可信"，不应副作用地把原行撤销；
        // 撤销状态由显式 Logout / RevokeAll 维护，避免与锁口/改密两条路径互相干扰。
        Assert.Null(original.RevokedAt);
    }

    private async Task ChangePasswordAsync(Guid userId)
    {
        using var scope = _rootProvider.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException("Seed user vanished.");
        var result = await userManager.ChangePasswordAsync(user, SeedPassword, "new-password-after-rotation");
        Assert.True(result.Succeeded);
    }

    private async Task<(Guid userId, string rawToken, Guid recordId)> SeedUserAndStampedTokenAsync(string rawToken)
    {
        using var scope = _rootProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TigerRagDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = $"revocation-test-{Guid.NewGuid():N}",
            PasswordSalt = "salt",
            CreatedAt = DateTimeOffset.UtcNow
        };
        var createResult = await userManager.CreateAsync(user, SeedPassword);
        if (!createResult.Succeeded)
        {
            throw new InvalidOperationException(
                "Seed user creation failed: " + string.Join("; ", createResult.Errors.Select(error => error.Description)));
        }

        var stamp = await userManager.GetSecurityStampAsync(user);
        var tokenHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(rawToken)));
        var record = new TigerRAG.Infrastructure.Persistence.Entities.refresh_token_record
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

        return (user.Id, rawToken, record.Id);
    }

    private async Task LockUserAsync(Guid userId, DateTimeOffset lockoutEnd)
    {
        using var scope = _rootProvider.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException("Seed user vanished.");
        var result = await userManager.SetLockoutEndDateAsync(user, lockoutEnd);
        Assert.True(result.Succeeded);
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
        return services.BuildServiceProvider();
    }

    private static async Task EnsureSchemaAsync(ServiceProvider provider)
    {
        // 连跑两个测试时 Postgres 偶发丢连接；EF Core 默认会重试一次瞬态错误，
        // 这里再包一层显式重试以保证 back-to-back DROP+CREATE 后能稳定建表。
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var scope = provider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<TigerRagDbContext>();
                await context.Database.EnsureCreatedAsync();
                return;
            }
            catch (System.Net.Sockets.SocketException) when (attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt));
            }
        }
    }

    private static async Task CreateTestDatabaseAsync()
    {
        await using var connection = new Npgsql.NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand(
            $"CREATE DATABASE \"{TestDatabaseName}\"", connection);
        // 42P04 = database already exists；上次跑挂时 DisposeAsync 没完成 DROP 会留下残骸，
        // 这里吞掉已存在错误，把幂等性放在 InitializeAsync 的入口而非 drop 顺序上。
        try
        {
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Npgsql.PostgresException error) when (error.SqlState == "42P04")
        {
        }
    }

    private static async Task DropTestDatabaseAsync()
    {
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