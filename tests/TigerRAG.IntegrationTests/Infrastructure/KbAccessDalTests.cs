using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TigerRAG.Application.KnowledgeBases;
using TigerRAG.Application.Shared;
using TigerRAG.Infrastructure.Dal;
using TigerRAG.Infrastructure.Identity;
using TigerRAG.Infrastructure.KnowledgeBases.Dal;
using TigerRAG.Infrastructure.Persistence;
using TigerRAG.Infrastructure.Persistence.Entities.Documents;
using TigerRAG.Infrastructure.Persistence.Entities.KnowledgeBases;

namespace TigerRAG.IntegrationTests.Infrastructure;

/// <summary>KbAccessDal 集成测试：并发 ACL 写入不产生并集残留 + 业务代码级联删除（KB 删除后 ACL 行同步清除）。</summary>
[Collection(nameof(PostgresCollection))]
public sealed class KbAccessDalTests : IAsyncLifetime
{
    private const string TestDatabaseName = "tigerrag_kb_acl_test";
    private static readonly string AdminConnectionString =
        "Host=localhost;Port=5432;Database=postgres;Username=tigerrag;Password=And@2088;Timeout=5";
    private static readonly string TestConnectionString =
        $"Host=localhost;Port=5432;Database={TestDatabaseName};Username=tigerrag;Password=And@2088;Timeout=5";

    private ServiceProvider _rootProvider = null!;
    private Guid _seedUserId;
    private Guid _seedRoleId;

    public async Task InitializeAsync()
    {
        if (!await LocalPostgresFixture.IsAvailableAsync())
            throw new InvalidOperationException("Local Postgres is not reachable.");

        Npgsql.NpgsqlConnection.ClearAllPools();
        await DropTestDatabaseAsync();
        await CreateTestDatabaseAsync();
        _rootProvider = BuildServiceProvider(TestConnectionString);
        await EnsureSchemaAsync(_rootProvider);
        _seedUserId = await SeedUserAsync();
        _seedRoleId = await SeedRoleAsync("KbViewer");
    }

    public async Task DisposeAsync()
    {
        if (_rootProvider is not null) await _rootProvider.DisposeAsync();
        await DropTestDatabaseAsync();
    }

    /// <summary>GetAccessibleKbIdsAsync：Owner ∪ User ACL ∪ Role ACL 三路并集，去重。</summary>
    [Fact]
    public async Task GetAccessibleKbIdsAsync_UnionOfOwnerAndUserAndRoleAcls()
    {
        await using var scope = _rootProvider.CreateAsyncScope();
        var dal = scope.ServiceProvider.GetRequiredService<IKbAccessDal>();
        var db = scope.ServiceProvider.GetRequiredService<TigerRagDbContext>();

        var otherUser = Guid.NewGuid();
        var ownedKb = await SeedKbAsync(db, _seedUserId);
        var otherKb = await SeedKbAsync(db, otherUser);
        // 授予 _seedUserId 访问 otherKb 的用户 ACL
        await db.KnowledgeBasePermissions.AddAsync(new knowledge_base_permission_record
        {
            KnowledgeBaseId = otherKb,
            PrincipalType = PermissionPrincipalType.User,
            PrincipalId = _seedUserId,
        });
        // 授予 _seedRoleId 访问同一 otherKb 的角色 ACL（冗余，验证去重）
        await db.KnowledgeBasePermissions.AddAsync(new knowledge_base_permission_record
        {
            KnowledgeBaseId = otherKb,
            PrincipalType = PermissionPrincipalType.Role,
            PrincipalId = _seedRoleId,
        });
        await db.SaveChangesAsync();

        var accessible = await dal.GetAccessibleKbIdsAsync(_seedUserId, ["KbViewer"], CancellationToken.None);

        Assert.Contains(ownedKb, accessible);
        Assert.Contains(otherKb, accessible);
        // 去重：otherKb 同时通过 User ACL 和 Role ACL 命中，只应出现一次
        Assert.Single(accessible, kb => kb == otherKb);
        Assert.Equal(2, accessible.Distinct().Count());
    }

    /// <summary>ReplacePermissionsAsync：整体替换，幂等；重复调用结果一致。</summary>
    [Fact]
    public async Task ReplacePermissionsAsync_OverwriteSemantics_SecondCallReplacesFirst()
    {
        await using var scope = _rootProvider.CreateAsyncScope();
        var dal = scope.ServiceProvider.GetRequiredService<IKbAccessDal>();
        var db = scope.ServiceProvider.GetRequiredService<TigerRagDbContext>();

        var kbId = await SeedKbAsync(db, _seedUserId);
        var user1 = await SeedUserAsync();
        var user2 = await SeedUserAsync();

        // 第一次：授权 user1
        await dal.ReplacePermissionsAsync(kbId, _seedUserId, isAdmin: false, [user1], [], CancellationToken.None);
        var snapshot1 = await dal.GetPermissionsAsync(kbId, CancellationToken.None);
        Assert.Equal([user1], snapshot1.UserIds);

        // 第二次：授权 user2（应整体替换，不应残留 user1）
        await dal.ReplacePermissionsAsync(kbId, _seedUserId, isAdmin: false, [user2], [], CancellationToken.None);
        var snapshot2 = await dal.GetPermissionsAsync(kbId, CancellationToken.None);
        Assert.Equal([user2], snapshot2.UserIds);
        Assert.DoesNotContain(user1, snapshot2.UserIds);
    }

    /// <summary>ReplacePermissionsAsync：非 Admin 非 Owner 调用应抛 UnauthorizedAccessException。</summary>
    [Fact]
    public async Task ReplacePermissionsAsync_NonOwnerNonAdmin_ThrowsUnauthorized()
    {
        await using var scope = _rootProvider.CreateAsyncScope();
        var dal = scope.ServiceProvider.GetRequiredService<IKbAccessDal>();
        var db = scope.ServiceProvider.GetRequiredService<TigerRagDbContext>();

        var ownerId = await SeedUserAsync();
        var kbId = await SeedKbAsync(db, ownerId);
        var intruder = await SeedUserAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            dal.ReplacePermissionsAsync(kbId, intruder, isAdmin: false, [], [], CancellationToken.None));
    }

    /// <summary>业务级联删除：KB 删除后，其 ACL 行由业务代码在事务内同步清除（无 FK 级联）。</summary>
    [Fact]
    public async Task DeletePermissionsAsync_AfterKbDeletion_AclRowsCleared()
    {
        await using var scope = _rootProvider.CreateAsyncScope();
        var dal = scope.ServiceProvider.GetRequiredService<IKbAccessDal>();
        var db = scope.ServiceProvider.GetRequiredService<TigerRagDbContext>();

        var kbId = await SeedKbAsync(db, _seedUserId);
        var otherUser = await SeedUserAsync();
        await dal.ReplacePermissionsAsync(kbId, _seedUserId, isAdmin: false, [otherUser], ["KbViewer"], CancellationToken.None);

        // 业务代码级联删除：先删 ACL，再删 KB
        await db.KnowledgeBasePermissions
            .Where(p => p.KnowledgeBaseId == kbId)
            .ExecuteDeleteAsync();
        await db.KnowledgeBases
            .Where(kb => kb.Id == kbId)
            .ExecuteDeleteAsync();

        // 验证 ACL 行已清除
        await using var verifyScope = _rootProvider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<TigerRagDbContext>();
        var remaining = await verifyDb.KnowledgeBasePermissions
            .CountAsync(p => p.KnowledgeBaseId == kbId);
        Assert.Equal(0, remaining);
    }

    // ── Helpers ──

    private async Task<Guid> SeedUserAsync()
    {
        await using var scope = _rootProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TigerRagDbContext>();
        var id = Guid.NewGuid();
        await db.Users.AddAsync(new AppUser
        {
            Id = id,
            UserName = $"user-{id:N}",
            NormalizedUserName = $"USER-{id:N}",
            PasswordHash = "test",
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> SeedRoleAsync(string roleName)
    {
        await using var scope = _rootProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TigerRagDbContext>();
        var id = Guid.NewGuid();
        await db.Roles.AddAsync(new IdentityRole<Guid>
        {
            Id = id,
            Name = roleName,
            NormalizedName = roleName.ToUpperInvariant(),
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> SeedKbAsync(TigerRagDbContext db, Guid ownerId)
    {
        var kbId = Guid.NewGuid();
        await db.KnowledgeBases.AddAsync(new knowledge_base_record
        {
            Id = kbId,
            Name = $"kb-{kbId:N}",
            Description = null,
            OwnerId = ownerId,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return kbId;
    }

    private static ServiceProvider BuildServiceProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddDbContext<TigerRagDbContext>(options => options.UseNpgsql(connectionString));
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
        services.AddScoped<TigerRagDbContext>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IKbAccessDal, KbAccessDal>();
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
