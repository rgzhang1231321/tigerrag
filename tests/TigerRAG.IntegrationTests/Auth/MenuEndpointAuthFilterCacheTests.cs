using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TigerRAG.Application.Auth;
using TigerRAG.Application.OperationAudit;
using TigerRAG.Application.Shared;
using TigerRAG.Application.Users;
using TigerRAG.Infrastructure.Persistence;
using TigerRAG.Infrastructure.Persistence.Entities.RoleEndpointGrants;
using TigerRAG.IntegrationTests.Infrastructure;

namespace TigerRAG.IntegrationTests.Api;

/// <summary>
/// 缓存装饰器 E2E：MenuEndpointAuthFilter 命中 Redis 缓存后不再访问 DB；grant 撤销后缓存立即失效。
/// 与 <see cref="TigerRAG.IntegrationTests.Api.RoleEndpointAuthorizationE2ETests"/> 共享 role_endpoint_grant 表，使用同一 xUnit collection 串行化以避免 Initialize 互相 wipe。
/// </summary>
[Collection("RoleEndpointGrantIntegration")]
public sealed class MenuEndpointAuthFilterCacheTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await TestDatabaseFixture.EnsureTestDatabaseAsync();
        _factory = BuildFactory();
        _client = _factory.CreateClient();

        await TestDatabaseFixture.EnsureSchemaAsync(_factory.Services);

        // 清空授权数据，避免历史测试残留造成 PK 冲突。
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TigerRagDbContext>();
        db.RoleEndpointGrants.RemoveRange(db.RoleEndpointGrants);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private static WebApplicationFactory<Program> BuildFactory()
    {
        var adminUser = new UserAccount(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "admin",
            ["Admin"])
        {
            SecurityStamp = "test-stamp",
        };

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:SigningKey", "test-only-signing-key-with-at-least-32-characters");
            builder.UseSetting("ConnectionStrings:Redis", "localhost:6379,abortConnect=false,connectTimeout=100,syncTimeout=100");
            builder.UseSetting("ConnectionStrings:PostgreSql", TestDatabaseFixture.TestConnectionString);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IUserDal>();
                services.AddSingleton<IUserDal>(new StubUserDal(adminUser));
                services.RemoveAll<IUserCredentialDal>();
                services.AddSingleton<IUserCredentialDal>(new StubCredentialDal());
                services.RemoveAll<IRefreshSessionDal>();
                services.AddSingleton<IRefreshSessionDal>(new StubRefreshSessionDal());
                services.RemoveAll<IOperationAuditWriter>();
                services.AddSingleton<IOperationAuditWriter>(new StubAuditWriter());
                // 注入计数 cache 替身：验证缓存命中/失效路径。
                services.RemoveAll<IRoleEndpointGrantCache>();
                services.AddSingleton<IRoleEndpointGrantCache>(new CountingGrantCache());
            });
        });
    }

    [Fact]
    public async Task SecondRequestForSameRole_HitsCacheWithoutInnerDb()
    {
        // 第一次请求：从 DB 加载并回填；第二次：直接命中缓存。
        // Filter 通过 = HTTP 200 且 body code != 40300（控制器层返回的 40400/0 等不算授权失败）。
        await SeedGrantAsync("Admin", "documents.permissions.replace", "documents");
        var token = await LoginAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var first = await PostEndpointAsync($"/api/documents/{Guid.NewGuid()}/permissions");
        var second = await PostEndpointAsync($"/api/documents/{Guid.NewGuid()}/permissions");

        Assert.NotEqual(40300, first);  // filter 通过
        Assert.NotEqual(40300, second);
        var cache = GetCache();
        // 两次请求都触发 GetRoleEndpointsAsync 查缓存；只有首次 miss 会回填（Set 仅调 1 次）。
        Assert.Equal(2, cache.GetCalls["Admin"]);
        Assert.Equal(1, cache.SetCalls["Admin"]);  // 第二次直接命中缓存，不再回填 inner DB
    }

    [Fact]
    public async Task GrantRevoke_InvalidatesCache_NextRequestForbidden()
    {
        // 灌一条 grant → 第一次请求回填缓存（filter 通过） → 撤销 → 缓存失效 → 第二次请求应被 filter 拒绝（40300）。
        await SeedGrantAsync("Admin", "documents.permissions.replace", "documents");
        var token = await LoginAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // 第一次：filter 通过（code 可能是 40400 因 doc 不存在，但 != 40300 即授权通过）
        var first = await PostEndpointAsync($"/api/documents/{Guid.NewGuid()}/permissions");
        Assert.NotEqual(40300, first);

        // 撤销 grant（直接 DB），缓存手动失效以模拟真实 service 写路径
        await RevokeGrantAsync("Admin", "documents.permissions.replace");
        await GetCache().InvalidateRoleAsync("Admin", CancellationToken.None);

        // 第二次：filter 拒绝（40300）
        var second = await PostEndpointAsync($"/api/documents/{Guid.NewGuid()}/permissions");
        Assert.Equal(40300, second);
    }

    private async Task<int> PostEndpointAsync(string url)
    {
        var response = await _client.PostAsJsonAsync(url, new
        {
            userIds = Array.Empty<Guid>(),
            roles = Array.Empty<string>()
        });
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);  // 统一外壳 HTTP 200
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("code").GetInt32();
    }

    private async Task<string> LoginAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            userName = "admin",
            passwordHash = new string('a', 32),
        });
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("data").GetProperty("accessToken").GetString()!;
    }

    private async Task SeedGrantAsync(string role, string endpoint, string menu)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TigerRagDbContext>();
        db.RoleEndpointGrants.Add(new role_endpoint_grant_record
        {
            RoleName = role,
            MenuKey = menu,
            EndpointKey = endpoint,
            GrantedAt = DateTimeOffset.UtcNow,
            GrantedBy = Guid.Empty,
        });
        await db.SaveChangesAsync();
    }

    private async Task RevokeGrantAsync(string role, string endpoint)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TigerRagDbContext>();
        var row = db.RoleEndpointGrants.FirstOrDefault(g => g.RoleName == role && g.EndpointKey == endpoint);
        if (row is not null)
        {
            db.RoleEndpointGrants.Remove(row);
            await db.SaveChangesAsync();
        }
    }

    private CountingGrantCache GetCache()
    {
        return (CountingGrantCache)_factory.Services.GetRequiredService<IRoleEndpointGrantCache>();
    }

    // ── Stubs ──────────────────────────────────────────────────────────

    private sealed class CountingGrantCache : IRoleEndpointGrantCache
    {
        private readonly Dictionary<string, HashSet<string>> _store = new(StringComparer.Ordinal);

        public Dictionary<string, int> GetCalls { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> SetCalls { get; } = new(StringComparer.Ordinal);
        public int InvalidateCalls { get; private set; }

        public Task<IReadOnlySet<string>?> GetRoleEndpointsAsync(string roleName, CancellationToken ct)
        {
            GetCalls.TryGetValue(roleName, out var c);
            GetCalls[roleName] = c + 1;
            if (_store.TryGetValue(roleName, out var s) && s.Count > 0)
                return Task.FromResult<IReadOnlySet<string>?>(new HashSet<string>(s, StringComparer.Ordinal));
            return Task.FromResult<IReadOnlySet<string>?>(null);
        }

        public Task SetRoleEndpointsAsync(string roleName, IReadOnlyCollection<string> endpointKeys, CancellationToken ct)
        {
            SetCalls.TryGetValue(roleName, out var c);
            SetCalls[roleName] = c + 1;
            _store[roleName] = new HashSet<string>(endpointKeys, StringComparer.Ordinal);
            return Task.CompletedTask;
        }

        public Task InvalidateRoleAsync(string roleName, CancellationToken ct)
        {
            InvalidateCalls++;
            _store.Remove(roleName);
            return Task.CompletedTask;
        }
    }

    private sealed class StubUserDal(UserAccount loginUser) : IUserDal
    {
        public Task<UserAccount?> ValidateCredentialsAsync(string userName, string passwordHash, CancellationToken ct) =>
            Task.FromResult<UserAccount?>(loginUser);

        public Task<string?> GetPasswordSaltAsync(string userName, CancellationToken ct) =>
            Task.FromResult<string?>("testsalt");

        public Task<IReadOnlyList<UserListItem>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<UserListItem>>([new(loginUser.Id, loginUser.UserName, loginUser.Roles, false)]);

        public Task<RevocationSnapshot?> GetRevocationSnapshotAsync(Guid userId, CancellationToken ct) =>
            Task.FromResult<RevocationSnapshot?>(loginUser.Id == userId
                ? new RevocationSnapshot(loginUser.SecurityStamp, false)
                : null);

        public Task AssignRolesAsync(Guid userId, IReadOnlyCollection<string> roles, CancellationToken ct) =>
            Task.CompletedTask;
    }

    private sealed class StubCredentialDal : IUserCredentialDal
    {
        public Task<UserAccount> CreateAsync(string userName, IReadOnlyCollection<string> roles, CancellationToken ct) =>
            Task.FromResult(new UserAccount(Guid.NewGuid(), userName, roles.ToArray()) { SecurityStamp = "test-stamp" });
        public Task SetInitialPasswordAsync(Guid userId, string passwordHash, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> ChangePasswordAsync(Guid userId, string current, string newHash, CancellationToken ct) => Task.FromResult(true);
        public Task ResetPasswordAsync(Guid userId, string newHash, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> DeleteAsync(Guid userId, CancellationToken ct) => Task.FromResult(true);
        public Task SetLockoutAsync(Guid userId, DateTimeOffset? lockoutEnd, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class StubRefreshSessionDal : IRefreshSessionDal
    {
        public Task<RefreshToken> CreateAsync(Guid userId, CancellationToken ct) =>
            Task.FromResult(new RefreshToken("refresh-token", DateTimeOffset.UtcNow.AddDays(7)));
        public Task<RefreshSession?> RotateAsync(string value, CancellationToken ct) =>
            Task.FromResult<RefreshSession?>(null);
        public Task RevokeAsync(string value, CancellationToken ct) => Task.CompletedTask;
        public Task RevokeAllAsync(Guid userId, CancellationToken ct) => Task.CompletedTask;
        public Task<UserAccount?> GetUserByTokenAsync(string value, CancellationToken ct) =>
            Task.FromResult<UserAccount?>(null);
    }

    private sealed class StubAuditWriter : IOperationAuditWriter
    {
        public Task RecordAsync(OperationAuditEntry entry, CancellationToken ct) => Task.CompletedTask;
    }
}
