using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TigerRAG.Application.Security;
using TigerRAG.Domain.Documents;
using TigerRAG.Infrastructure.Dal;
using TigerRAG.Infrastructure.Identity;
using TigerRAG.Infrastructure.Persistence;
using TigerRAG.Infrastructure.Persistence.Entities;

namespace TigerRAG.IntegrationTests.Infrastructure;

/// <summary>
/// 验证角色与文档 ACL 的"覆盖式更新"不会因为读旧集 → 计算差集 → 保存的窗口而出现并集残留。
/// 两次并发调用各自的期望集互不相交时，最终状态必须严格是其中之一，且必须包含至少一次调用要求"删除"的主体。
/// 必须在本地 Postgres 上跑；无 DB 时 fail loud。
/// </summary>
public sealed class RoleAndAclConcurrencyTests : IAsyncLifetime
{
    private const string TestDatabaseName = "tigerrag_concurrency_test";
    private static readonly string AdminConnectionString =
        "Host=localhost;Port=5432;Database=postgres;Username=tigerrag;Password=And@2088;Timeout=5";
    private static readonly string TestConnectionString =
        $"Host=localhost;Port=5432;Database={TestDatabaseName};Username=tigerrag;Password=And@2088;Timeout=5";

    private ServiceProvider _rootProvider = null!;

    public async Task InitializeAsync()
    {
        if (!await LocalPostgresFixture.IsAvailableAsync())
        {
            throw new InvalidOperationException("Local Postgres is not reachable; RoleAndAclConcurrencyTests requires it.");
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
    public async Task AssignRolesAsync_TwoConcurrentReplacements_NeverProduceUnion()
    {
        var userId = await SeedUserAsync($"user-{Guid.NewGuid():N}");

        // 跑多轮以确保能稳定触发竞态；只要一轮出现并集即视为修复缺失。
        for (var iteration = 0; iteration < 10; iteration++)
        {
            await ResetUserRolesAsync(userId);

            using var scope1 = _rootProvider.CreateScope();
            using var scope2 = _rootProvider.CreateScope();
            var dal1 = scope1.ServiceProvider.GetRequiredService<IUserDal>();
            var dal2 = scope2.ServiceProvider.GetRequiredService<IUserDal>();

            // 互斥期望：一边要 Editor，一边要 Viewer；任何最终包含两者即为残留。
            var task1 = Task.Run(() => dal1.AssignRolesAsync(userId, [SystemRoles.Editor], CancellationToken.None));
            var task2 = Task.Run(() => dal2.AssignRolesAsync(userId, [SystemRoles.Viewer], CancellationToken.None));
            await Task.WhenAll(task1, task2);

            var assigned = await ReadRoleNamesAsync(userId);
            Assert.True(
                assigned.Count == 1,
                $"Iteration {iteration}: expected exactly one role, got [{string.Join(",", assigned)}] (union race leaked).");
            Assert.True(
                assigned[0] == SystemRoles.Editor || assigned[0] == SystemRoles.Viewer,
                $"Iteration {iteration}: unexpected role [{string.Join(",", assigned)}].");
        }
    }

    [Fact]
    public async Task ReplacePermissionsAsync_TwoConcurrentReplacementsFromEmpty_NeverProduceUnion()
    {
        var (documentId, _) = await SeedDocumentAsync($"empty-{Guid.NewGuid():N}");

        for (var iteration = 0; iteration < 10; iteration++)
        {
            await ResetDocumentPermissionsAsync(documentId);
            var u1 = Guid.NewGuid();
            var u2 = Guid.NewGuid();
            // DAL 要求 userIds 必须已存在于 AspNetUsers；先注册，再走覆盖式写入。
            await SeedUserByIdAsync(u1, $"acl-u1-{u1:N}");
            await SeedUserByIdAsync(u2, $"acl-u2-{u2:N}");

            using var scope1 = _rootProvider.CreateScope();
            using var scope2 = _rootProvider.CreateScope();
            var dal1 = scope1.ServiceProvider.GetRequiredService<IDocumentAccessDal>();
            var dal2 = scope2.ServiceProvider.GetRequiredService<IDocumentAccessDal>();

            // 双方都从空开始追加；最终不应同时包含 u1 与 u2。
            var task1 = Task.Run(() => dal1.ReplacePermissionsAsync(documentId, Guid.NewGuid(), isAdmin: true, [u1], [], CancellationToken.None));
            var task2 = Task.Run(() => dal2.ReplacePermissionsAsync(documentId, Guid.NewGuid(), isAdmin: true, [u2], [], CancellationToken.None));
            await Task.WhenAll(task1, task2);

            var principals = await ReadPrincipalUserIdsAsync(documentId);
            Assert.True(
                principals.Count == 1,
                $"Iteration {iteration}: expected exactly one principal, got [{string.Join(",", principals)}] (union race leaked).");
        }
    }

    [Fact]
    public async Task ReplacePermissionsAsync_TwoConcurrentExclusiveReplacements_NeverLeaveStaleResidue()
    {
        var (documentId, _) = await SeedDocumentAsync($"exclusive-{Guid.NewGuid():N}");
        var staleUser = Guid.NewGuid();
        await SeedUserByIdAsync(staleUser, $"stale-{Guid.NewGuid():N}");

        for (var iteration = 0; iteration < 10; iteration++)
        {
            await ResetDocumentPermissionsAsync(documentId);
            await SeedUserPermissionAsync(documentId, staleUser);

            var uNew = Guid.NewGuid();
            var uOther = Guid.NewGuid();
            await SeedUserByIdAsync(uNew, $"acl-uNew-{uNew:N}");
            await SeedUserByIdAsync(uOther, $"acl-uOther-{uOther:N}");

            using var scope1 = _rootProvider.CreateScope();
            using var scope2 = _rootProvider.CreateScope();
            var dal1 = scope1.ServiceProvider.GetRequiredService<IDocumentAccessDal>();
            var dal2 = scope2.ServiceProvider.GetRequiredService<IDocumentAccessDal>();

            // 双方 diff 都会要求删除 staleUser；任一方提交后 staleUser 都应已消失。
            var task1 = Task.Run(() => dal1.ReplacePermissionsAsync(documentId, Guid.NewGuid(), isAdmin: true, [uNew], [], CancellationToken.None));
            var task2 = Task.Run(() => dal2.ReplacePermissionsAsync(documentId, Guid.NewGuid(), isAdmin: true, [uOther], [], CancellationToken.None));
            await Task.WhenAll(task1, task2);

            var principals = await ReadPrincipalUserIdsAsync(documentId);
            Assert.DoesNotContain(
                staleUser,
                principals);
            Assert.True(
                principals.Count <= 1,
                $"Iteration {iteration}: staleUser removed but union of new entries leaked: [{string.Join(",", principals)}].");
        }
    }

    // ----- helpers -----

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
        services.AddScoped<IDocumentAccessDal, DocumentAccessDal>();
        return services.BuildServiceProvider();
    }

    private static async Task EnsureSchemaAsync(ServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TigerRagDbContext>();
        await context.Database.EnsureCreatedAsync();
    }

    private async Task<Guid> SeedUserAsync(string userName)
    {
        var userId = Guid.NewGuid();
        await SeedUserByIdAsync(userId, userName);
        return userId;
    }

    private async Task SeedUserByIdAsync(Guid userId, string userName)
    {
        using var scope = _rootProvider.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser
        {
            Id = userId,
            UserName = userName,
            PasswordSalt = "salt",
            CreatedAt = DateTimeOffset.UtcNow
        };
        var result = await userManager.CreateAsync(user, "placeholder-password");
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "Seed user creation failed: " + string.Join("; ", result.Errors.Select(error => error.Description)));
        }
    }

    private async Task<(Guid documentId, Guid knowledgeBaseId)> SeedDocumentAsync(string fileName)
    {
        using var scope = _rootProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TigerRagDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        var owner = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = $"doc-owner-{Guid.NewGuid():N}",
            PasswordSalt = "salt",
            CreatedAt = DateTimeOffset.UtcNow
        };
        var ownerResult = await userManager.CreateAsync(owner, "placeholder-password");
        if (!ownerResult.Succeeded)
        {
            throw new InvalidOperationException(
                "Seed owner creation failed: " + string.Join("; ", ownerResult.Errors.Select(error => error.Description)));
        }

        var kbId = Guid.NewGuid();
        var docId = Guid.NewGuid();
        context.KnowledgeBases.Add(new knowledge_base_record
        {
            Id = kbId,
            Name = "kb-" + Guid.NewGuid().ToString("N"),
            OwnerId = owner.Id,
            CreatedAt = DateTimeOffset.UtcNow
        });
        context.Documents.Add(new document_record
        {
            Id = docId,
            KnowledgeBaseId = kbId,
            FileName = fileName,
            StoragePath = "stub/" + Guid.NewGuid().ToString("N"),
            Status = DocumentStatus.Pending,
            ChunkCount = 0,
            CreatedBy = owner.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();
        return (docId, kbId);
    }

    private async Task SeedUserPermissionAsync(Guid documentId, Guid userId)
    {
        using var scope = _rootProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TigerRagDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        var exists = await userManager.FindByIdAsync(userId.ToString());
        if (exists is null)
        {
            var seed = new AppUser
            {
                Id = userId,
                UserName = $"acl-user-{userId:N}",
                PasswordSalt = "salt",
                CreatedAt = DateTimeOffset.UtcNow
            };
            var result = await userManager.CreateAsync(seed, "placeholder-password");
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    "Seed user creation failed: " + string.Join("; ", result.Errors.Select(error => error.Description)));
            }
        }

        context.DocumentPermissions.Add(new document_permission_record
        {
            DocumentId = documentId,
            PrincipalType = PermissionPrincipalType.User,
            PrincipalId = userId
        });
        await context.SaveChangesAsync();
    }

    private async Task ResetUserRolesAsync(Guid userId)
    {
        using var scope = _rootProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TigerRagDbContext>();
        await context.UserRoles
            .Where(userRole => userRole.UserId == userId)
            .ExecuteDeleteAsync();
    }

    private async Task ResetDocumentPermissionsAsync(Guid documentId)
    {
        using var scope = _rootProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TigerRagDbContext>();
        await context.DocumentPermissions
            .Where(permission => permission.DocumentId == documentId)
            .ExecuteDeleteAsync();
    }

    private async Task<List<string>> ReadRoleNamesAsync(Guid userId)
    {
        using var scope = _rootProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TigerRagDbContext>();
        return await context.UserRoles
            .Where(userRole => userRole.UserId == userId)
            .Join(context.Roles, userRole => userRole.RoleId, role => role.Id, (userRole, role) => role.Name!)
            .ToListAsync();
    }

    private async Task<List<Guid>> ReadPrincipalUserIdsAsync(Guid documentId)
    {
        using var scope = _rootProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TigerRagDbContext>();
        return await context.DocumentPermissions
            .Where(permission => permission.DocumentId == documentId && permission.PrincipalType == PermissionPrincipalType.User)
            .Select(permission => permission.PrincipalId)
            .ToListAsync();
    }

    private static async Task CreateTestDatabaseAsync()
    {
        await using var connection = new Npgsql.NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand($"CREATE DATABASE \"{TestDatabaseName}\"", connection);
        await cmd.ExecuteNonQueryAsync();
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

        await using var drop = new Npgsql.NpgsqlCommand($"DROP DATABASE IF EXISTS \"{TestDatabaseName}\"", connection);
        await drop.ExecuteNonQueryAsync();
    }
}