using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TigerRAG.Application.Auth;
using TigerRAG.Application.Documents;
using TigerRAG.Application.OperationAudit;
using TigerRAG.Application.Shared;
using TigerRAG.Application.Users;
using TigerRAG.Infrastructure.Persistence;
using TigerRAG.Infrastructure.Persistence.Entities.RoleEndpointGrants;
using TigerRAG.IntegrationTests.Api;
using TigerRAG.IntegrationTests.Infrastructure;

namespace TigerRAG.IntegrationTests.Api;

/// <summary>DocumentsController + DocumentPermissionsController 权限矩阵集成测试：验证授权 filter 对文档端点的 grant/deny 行为。</summary>
[Collection("PermissionMatrixIntegration")]
public sealed class DocumentControllerPermissionTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await TestDatabaseFixture.EnsureTestDatabaseAsync();
        _factory = BuildFactory();
        _client = _factory.CreateClient();
        await TestDatabaseFixture.EnsureSchemaAsync(_factory.Services);

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

    private async Task<string> LoginAsync(string userName)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            userName,
            passwordHash = new string('a', 32),
        });
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("data").GetProperty("accessToken").GetString()!;
    }

    private async Task GrantAsync(string roleName, string endpointKey, string menuKey = "documents")
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TigerRagDbContext>();
        db.RoleEndpointGrants.Add(new role_endpoint_grant_record
        {
            RoleName = roleName,
            MenuKey = menuKey,
            EndpointKey = endpointKey,
            GrantedAt = DateTimeOffset.UtcNow,
            GrantedBy = Guid.Empty,
        });
        await db.SaveChangesAsync();
    }

    private static WebApplicationFactory<Program> BuildFactory()
    {
        var adminUser = new UserAccount(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
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
                services.RemoveAll<IDocumentAccessDal>();
                services.AddSingleton<IDocumentAccessDal>(new StubDocumentAccessDal());
            });
        });
    }

    // ── DocumentsController 端点 ──

    [Fact]
    public async Task Admin_WithListGrant_CanListDocuments()
    {
        await GrantAsync("Admin", "documents.list");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync("admin"));

        // KB 不存在时返回 NotFound，但关键是授权已通过（code != 403）。
        var response = await _client.PostAsJsonAsync("/api/documents/list", new { kbId = Guid.NewGuid() });
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        // 授权通过但业务校验失败（KB 不存在），code 应为 404 而非 403。
        var code = body.RootElement.GetProperty("code").GetInt32();
        Assert.True(code == 40400, $"Expected 40400 (NotFound), got {code}");
    }

    [Fact]
    public async Task Admin_WithGetGrant_CanGetDocument()
    {
        await GrantAsync("Admin", "documents.get");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync("admin"));

        var response = await _client.PostAsync($"/api/documents/{Guid.NewGuid()}", null);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Admin_WithDeleteGrant_CanDeleteDocument()
    {
        await GrantAsync("Admin", "documents.delete");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync("admin"));

        var response = await _client.PostAsync($"/api/documents/{Guid.NewGuid()}/delete", null);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Admin_WithBatchDeleteGrant_CanBatchDeleteDocuments()
    {
        await GrantAsync("Admin", "documents.batchDelete");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync("admin"));

        var response = await _client.PostAsJsonAsync("/api/documents/batch-delete", new { ids = new[] { Guid.NewGuid() } });
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Admin_WithContentGrant_CanGetContent()
    {
        await GrantAsync("Admin", "documents.content");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync("admin"));

        var response = await _client.PostAsync($"/api/documents/{Guid.NewGuid()}/content", null);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    // ── DocumentsController 无 grant 拒绝 ──

    [Fact]
    public async Task Admin_WithoutListGrant_Denied()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync("admin"));

        var response = await _client.PostAsJsonAsync("/api/documents/list", new { kbId = Guid.NewGuid() });
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("flag").GetBoolean());
    }

    [Fact]
    public async Task Admin_WithoutDeleteGrant_Denied()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync("admin"));

        var response = await _client.PostAsync($"/api/documents/{Guid.NewGuid()}/delete", null);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("flag").GetBoolean());
    }

    // ── DocumentPermissionsController 端点 ──

    [Fact]
    public async Task Admin_WithPermissionsReplaceGrant_CanReplacePermissions()
    {
        await GrantAsync("Admin", "documents.permissions.replace");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync("admin"));

        var response = await _client.PostAsJsonAsync($"/api/documents/{Guid.NewGuid()}/permissions", new
        {
            userIds = Array.Empty<Guid>(),
            roles = Array.Empty<string>(),
        });
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Admin_WithPermissionsGetGrant_CanGetPermissions()
    {
        await GrantAsync("Admin", "documents.permissions.get");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync("admin"));

        var response = await _client.GetAsync($"/api/documents/{Guid.NewGuid()}/permissions");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    // ── DocumentPermissionsController 无 grant 拒绝 ──

    [Fact]
    public async Task Admin_WithoutPermissionsReplaceGrant_Denied()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync("admin"));

        var response = await _client.PostAsJsonAsync($"/api/documents/{Guid.NewGuid()}/permissions", new
        {
            userIds = Array.Empty<Guid>(),
            roles = Array.Empty<string>(),
        });
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("flag").GetBoolean());
    }

    // ── 匿名用户被拒绝 ──

    [Fact]
    public async Task Anonymous_User_Denied_DocumentsList()
    {
        var response = await _client.PostAsJsonAsync("/api/documents/list", new { kbId = Guid.NewGuid() });
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("code").GetInt32() > 0);
    }

    [Fact]
    public async Task Anonymous_User_Denied_PermissionsReplace()
    {
        var response = await _client.PostAsJsonAsync($"/api/documents/{Guid.NewGuid()}/permissions", new
        {
            userIds = Array.Empty<Guid>(),
            roles = Array.Empty<string>(),
        });
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("code").GetInt32() > 0);
    }

    // ── Stubs ──

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

    private sealed class StubDocumentAccessDal : IDocumentAccessDal
    {
        public Task<IReadOnlyList<Guid>> GetAccessibleDocumentIdsAsync(Guid userId, IReadOnlyCollection<string> roles, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task ReplacePermissionsAsync(Guid documentId, Guid actorId, bool isAdmin, IReadOnlyCollection<Guid> userIds, IReadOnlyCollection<string> roles, CancellationToken ct) =>
            Task.CompletedTask;

        public Task<DocumentPermissionsSnapshot> GetPermissionsAsync(Guid documentId, CancellationToken ct) =>
            Task.FromResult(new DocumentPermissionsSnapshot([], []));

        public Task<Guid?> GetDocumentOwnerIdAsync(Guid documentId, CancellationToken ct) =>
            Task.FromResult<Guid?>(null);
    }
}
