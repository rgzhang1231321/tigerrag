using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TigerRAG.Application.Auth;
using TigerRAG.Application.OperationAudit;
using TigerRAG.Application.Shared;
using TigerRAG.Application.Users;
using TigerRAG.Infrastructure.Persistence;
using TigerRAG.Infrastructure.Persistence.Entities.RoleEndpointGrants;
using TigerRAG.IntegrationTests.Infrastructure;

namespace TigerRAG.IntegrationTests.Api;

/// <summary>角色-Endpoint 授权端到端行为：Admin 默认全通（由 bootstrap 灌 grant）、所有角色（含 Admin）平等可配置、无 grant 拒绝。与其他共享 role_endpoint_grant 表的 E2E 测试同 collection 串行化。</summary>
[Collection("RoleEndpointGrantIntegration")]
public sealed class RoleEndpointAuthorizationE2ETests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await TestDatabaseFixture.EnsureTestDatabaseAsync();
        _factory = BuildFactory();
        _client = _factory.CreateClient();

        await TestDatabaseFixture.EnsureSchemaAsync(_factory.Services);

        // 清空授权数据，测试中按需添加。
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
            });
        });
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

    [Fact]
    public async Task Admin_WithGrants_CanAccessDocuments()
    {
        // Admin 持有 grant 时能访问文档接口。
        await SeedAdminGrantAsync("documents.permissions.replace");
        var token = await LoginAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsJsonAsync($"/api/documents/{Guid.NewGuid()}/permissions", new
        {
            userIds = Array.Empty<Guid>(),
            roles = Array.Empty<string>()
        });
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_Returns403Envelope()
    {
        var response = await _client.PostAsJsonAsync($"/api/documents/{Guid.NewGuid()}/permissions", new
        {
            userIds = Array.Empty<Guid>(),
            roles = Array.Empty<string>()
        });
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(40300, body.RootElement.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Admin_WithoutGrant_Denied()
    {
        // Admin 没有 grant 时也应被拒绝（bypass 已移除）。
        var token = await LoginAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsJsonAsync($"/api/documents/{Guid.NewGuid()}/permissions", new
        {
            userIds = Array.Empty<Guid>(),
            roles = Array.Empty<string>()
        });
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("flag").GetBoolean());
    }

    [Fact]
    public async Task AdminRole_CanBeConfigured_ViaGrantMenu()
    {
        // Admin 与其他角色平等，可配置授权。
        await SeedAdminGrantAsync("roles.grants.grantMenu", "roles");
        var token = await LoginAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsJsonAsync("/api/roles/Admin/menus/documents/grant", new { });
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("flag").GetBoolean());
    }

    [Fact]
    public async Task AdminRole_CanBeConfigured_ViaListGrants()
    {
        await SeedAdminGrantAsync("roles.grants.list", "roles");
        var token = await LoginAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsJsonAsync("/api/roles/Admin/grants", new { });
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("flag").GetBoolean());
    }

    [Fact]
    public async Task AdminRole_CanBeConfigured_ViaRevokeMenu()
    {
        await SeedAdminGrantAsync("documents.get");
        await SeedAdminGrantAsync("roles.grants.revokeMenu", "roles");
        var token = await LoginAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsJsonAsync("/api/roles/Admin/menus/documents/revoke", new { });
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("flag").GetBoolean());
    }

    [Fact]
    public async Task AdminRole_CanBeConfigured_ViaToggleEndpoint()
    {
        await SeedAdminGrantAsync("roles.grants.toggle", "roles");
        var token = await LoginAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsJsonAsync("/api/roles/Admin/endpoints/toggle", new
        {
            endpointKey = "documents.get",
            menuKey = "documents",
            grant = true,
        });
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("flag").GetBoolean());
    }

    [Fact]
    public async Task Revoke_ImmediatelyDeniesAccess()
    {
        // 撤销 Admin 的 grant 后立即拒绝访问（验证 bypass 已移除 + 失效生效）。
        await SeedAdminGrantAsync("roles.grants.list", "roles");
        await SeedAdminGrantAsync("roles.grants.toggle", "roles");
        var token = await LoginAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // 先确认能访问
        var okResponse = await _client.PostAsJsonAsync("/api/roles/Viewer/grants", new { });
        using var okBody = JsonDocument.Parse(await okResponse.Content.ReadAsStringAsync());
        Assert.Equal(0, okBody.RootElement.GetProperty("code").GetInt32());

        // 撤销 grant
        var revokeResponse = await _client.PostAsJsonAsync("/api/roles/Admin/endpoints/toggle", new
        {
            endpointKey = "roles.grants.list",
            menuKey = "roles",
            grant = false,
        });
        revokeResponse.EnsureSuccessStatusCode();

        // 再次访问应被拒绝
        var deniedResponse = await _client.PostAsJsonAsync("/api/roles/Viewer/grants", new { });
        using var deniedBody = JsonDocument.Parse(await deniedResponse.Content.ReadAsStringAsync());
        Assert.False(deniedBody.RootElement.GetProperty("flag").GetBoolean());
    }

    private async Task SeedAdminGrantAsync(string endpointKey, string menuKey = "documents")
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TigerRagDbContext>();
        db.RoleEndpointGrants.Add(new role_endpoint_grant_record
        {
            RoleName = "Admin",
            MenuKey = menuKey,
            EndpointKey = endpointKey,
            GrantedAt = DateTimeOffset.UtcNow,
            GrantedBy = Guid.Empty,
        });
        await db.SaveChangesAsync();
    }

    // ── Stubs (mirroring ApiContractTests.cs) ──

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
