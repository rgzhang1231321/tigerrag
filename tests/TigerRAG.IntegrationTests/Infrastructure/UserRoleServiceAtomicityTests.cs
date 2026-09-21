using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TigerRAG.Application.Auth;
using TigerRAG.Application.Menus;
using TigerRAG.Application.OperationAudit;
using TigerRAG.Application.Roles;
using TigerRAG.Application.Shared;
using TigerRAG.Application.Users;
using TigerRAG.Infrastructure.Auth.Dal;
using TigerRAG.Infrastructure.Dal;
using TigerRAG.Infrastructure.Identity;
using TigerRAG.Infrastructure.Menus.Dal;
using TigerRAG.Infrastructure.OperationAudit.Dal;
using TigerRAG.Infrastructure.Persistence;
using TigerRAG.Infrastructure.Roles.Dal;
using TigerRAG.Infrastructure.Users;
using TigerRAG.IntegrationTests;

namespace TigerRAG.IntegrationTests.Infrastructure;
/// <summary>
/// 验证 <see cref="UserRoleService"/> 的"改凭据 → 换 stamp → 撤销刷新会话"三步原子性。
/// 当 stamp 轮换阶段抛异常时，已落库的凭据变更必须被回滚，DB 状态与调用前一致。
/// 必须在本地 Postgres 上跑；无 DB 时 fail loud。
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class UserRoleServiceAtomicityTests : IAsyncLifetime
{
    private const string TestDatabaseName = "tigerrag_atomicity_test";
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
            throw new InvalidOperationException("Local Postgres is not reachable; UserRoleServiceAtomicityTests requires it.");
        }

        // 跨测试实例 DropTestDatabaseAsync 会通过 pg_terminate_backend 杀掉残留连接；
        // 强制清空 Npgsql 池，避免下个测试拿到已死的连接引发 EnsureCreatedAsync 失败。
        Npgsql.NpgsqlConnection.ClearAllPools();
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
    public async Task ResetPasswordAsync_WhenStampRotationFails_PasswordRolledBack()
    {
        var userId = await SeedUserAsync();
        var originalHash = await ReadPasswordHashAsync(userId);

        var service = BuildUserServiceWithThrowingRotator();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ResetPasswordAsync(userId, "0dcc597e8857208c3a2606103a9d3d0e", CancellationToken.None));

        // 三步在同一事务内：stamp 轮换失败 → 整体回滚，密码哈希应保持不变。
        var currentHash = await ReadPasswordHashAsync(userId);
        Assert.Equal(originalHash, currentHash);
    }

    [Fact]
    public async Task SetInitialPasswordAsync_WhenStampRotationFails_PasswordRolledBack()
    {
        var userId = await SeedUserWithoutPasswordAsync();
        var originalHash = await ReadPasswordHashAsync(userId);

        var service = BuildUserServiceWithThrowingRotator();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SetInitialPasswordAsync(userId, "0dcc597e8857208c3a2606103a9d3d0e", CancellationToken.None));

        var currentHash = await ReadPasswordHashAsync(userId);
        Assert.Equal(originalHash, currentHash);
    }

    [Fact]
    public async Task SetLockoutAsync_WhenStampRotationFails_LockoutRolledBack()
    {
        var userId = await SeedUserAsync();
        var originalLockout = await ReadLockoutEndAsync(userId);

        var service = BuildUserServiceWithThrowingRotator();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SetLockoutAsync(userId, DateTimeOffset.UtcNow.AddYears(100), CancellationToken.None));

        var currentLockout = await ReadLockoutEndAsync(userId);
        Assert.Equal(originalLockout, currentLockout);
    }

    // ----- helpers -----

    private UserRoleService BuildUserServiceWithThrowingRotator()
    {
        var scope = _rootProvider.CreateScope();
        // scope 由调用方负责释放；此处为测试简洁起见不Dispose，进程退出时随 rootProvider 清理。
        var users = scope.ServiceProvider.GetRequiredService<IUserDal>();
        var credentials = scope.ServiceProvider.GetRequiredService<IUserCredentialDal>();
        var sessions = scope.ServiceProvider.GetRequiredService<IRefreshSessionDal>();
        var menuConfigs = scope.ServiceProvider.GetRequiredService<IMenuConfigDal>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var roleAdmin = scope.ServiceProvider.GetRequiredService<IRoleAdmin>();
        return new UserRoleService(
            users, credentials, sessions, menuConfigs, new ThrowingStampRotator(), uow, roleAdmin);
    }

    private async Task<Guid> SeedUserAsync()
    {
        using var scope = _rootProvider.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = $"atomicity-test-{Guid.NewGuid():N}",
            PasswordSalt = "salt",
            CreatedAt = DateTimeOffset.UtcNow
        };
        var result = await userManager.CreateAsync(user, SeedPassword);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "Seed user creation failed: " + string.Join("; ", result.Errors.Select(error => error.Description)));
        }

        return user.Id;
    }

    private async Task<Guid> SeedUserWithoutPasswordAsync()
    {
        using var scope = _rootProvider.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = $"atomicity-nopwd-{Guid.NewGuid():N}",
            PasswordSalt = "salt",
            CreatedAt = DateTimeOffset.UtcNow
        };
        // 用随机占位密码创建，再通过 UpdateAsync 把 PasswordHash 清空，模拟"已建账号未设密码"的初始态。
        var result = await userManager.CreateAsync(user, "placeholder-password");
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "Seed user creation failed: " + string.Join("; ", result.Errors.Select(error => error.Description)));
        }

        return user.Id;
    }

    private async Task<string?> ReadPasswordHashAsync(Guid userId)
    {
        using var scope = _rootProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TigerRagDbContext>();
        var user = await context.Users.AsNoTracking().SingleAsync(user => user.Id == userId);
        return user.PasswordHash;
    }

    private async Task<DateTimeOffset?> ReadLockoutEndAsync(Guid userId)
    {
        using var scope = _rootProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TigerRagDbContext>();
        var user = await context.Users.AsNoTracking().SingleAsync(user => user.Id == userId);
        return user.LockoutEnd;
    }

    private static ServiceProvider BuildServiceProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddDbContext<TigerRagDbContext>(options => options.UseNpgsql(connectionString));
        // 注册 IAuthenticationSchemeProvider，否则 AddSignInManager() 解析会报缺服务；与生产注册保持一致。
        services.AddAuthentication();
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
        services.AddScoped<IUserDal, UserDal>();
        services.AddScoped<IUserCredentialDal, UserDal>();
        services.AddScoped<IRefreshSessionDal, RefreshSessionDal>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IMenuConfigDal, MenuConfigDal>();
        services.AddScoped<IRoleAdmin, RoleAdminDal>();
        services.AddScoped<RoleAdminService>();
        services.AddScoped<IOperationAuditDal, OperationAuditDal>();
        services.AddScoped<IOperationAuditWriter, OperationAuditDal>();
        return services.BuildServiceProvider();
    }

    private static async Task EnsureSchemaAsync(ServiceProvider provider)
    {
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

    /// <summary>
    /// 抛异常型 stamp 轮换器：模拟 stamp 轮换阶段失败，验证三步原子回滚。
    /// </summary>
    private sealed class ThrowingStampRotator : IUserSecurityStampRotator
    {
        public Task RotateAsync(Guid userId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("stamp rotation failed");

        public Task InvalidateAsync(Guid userId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("stamp invalidation failed");
    }
}
