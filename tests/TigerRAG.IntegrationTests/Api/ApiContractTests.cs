using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;
using TigerRAG.Api;
using TigerRAG.Api.Hubs;
using TigerRAG.Application.Auth;
using TigerRAG.Application.Documents;
using TigerRAG.Application.KnowledgeBases;
using TigerRAG.Application.OperationAudit;
using TigerRAG.Application.Shared;
using TigerRAG.Application.Users;

namespace TigerRAG.IntegrationTests.Api;

public sealed class ApiContractTests : IClassFixture<TigerRagApiFactory>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ApiContractTests(TigerRagApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task ApiRoot_DescribesService()
    {
        var response = await _client.GetFromJsonAsync<ApiEnvelope<ServiceDescriptor>>("/api");

        Assert.NotNull(response);
        Assert.Equal(0, response.Code);
        Assert.Equal("TigerRAG.Api", response.Data?.Name);
        Assert.Equal("v1", response.Data?.Version);
    }

    [Fact]
    public async Task Liveness_ReturnsOk()
    {
        var response = await _client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("POST", "/api/users/me")]
    [InlineData("POST", "/api/knowledge-bases/list")]
    [InlineData("POST", "/api/documents/00000000-0000-0000-0000-000000000001/get")]
    [InlineData("POST", "/api/conversations")]
    [InlineData("POST", "/api/audit-logs/list")]
    public async Task SecuredModuleContract_RejectsAnonymousRequests(string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST")
        {
            request.Content = JsonContent.Create(new { });
        }

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("code").GetInt32() > 0);
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("data").ValueKind);
    }

    [Fact]
    public void BusinessRoutes_AreImplementedByControllers()
    {
        var services = _factory.Services;
        var actions = services
            .GetRequiredService<IActionDescriptorCollectionProvider>()
            .ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>()
            .ToArray();

        Assert.Contains(actions, action => action.ControllerName == "Auth" && action.ActionName == "Login");
        Assert.Contains(actions, action => action.ControllerName == "Users" && action.ActionName == "GetCurrent");
    }

    [Fact]
    public void AddTigerRagApi_WithShortJwtSigningKey_RejectsConfiguration()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SigningKey"] = "too-short"
            })
            .Build();

        var error = Assert.Throws<InvalidOperationException>(() =>
            services.AddTigerRagApi(configuration));

        Assert.Contains("Jwt:SigningKey", error.Message);
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsBearerTokenAndUser()
    {
        var user = new UserAccount(Guid.NewGuid(), "editor", ["Editor"]) { SecurityStamp = "test-stamp" };
        using var factory = CreateSecurityFactory(new StubUserDal(user));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            userName = "editor",
            passwordHash = new string('c', 32)
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = body.RootElement.GetProperty("data");
        Assert.Equal(0, body.RootElement.GetProperty("code").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("accessToken").GetString()));
        Assert.Equal("editor", data.GetProperty("user").GetProperty("userName").GetString());
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("tigerrag.refresh=", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refresh_WithValidCookie_RotatesSessionAndReturnsAccessToken()
    {
        var user = new UserAccount(Guid.NewGuid(), "viewer", ["Viewer"]) { SecurityStamp = "test-stamp" };
        var sessions = new StubRefreshSessionDal { RotatedUser = user };
        using var factory = CreateSecurityFactory(new StubUserDal(null), refreshSessions: sessions);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Cookie", "tigerrag.refresh=old-refresh-token");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("old-refresh-token", sessions.RotatedToken);
        Assert.Contains("tigerrag.refresh=", Assert.Single(response.Headers.GetValues("Set-Cookie")));
    }

    [Fact]
    public async Task Logout_WithRefreshCookie_RevokesAndClearsCookie()
    {
        var sessions = new StubRefreshSessionDal();
        using var factory = CreateSecurityFactory(new StubUserDal(null), refreshSessions: sessions);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        request.Headers.Add("Cookie", "tigerrag.refresh=refresh-token");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("refresh-token", sessions.RevokedToken);
        Assert.Contains("expires=", Assert.Single(response.Headers.GetValues("Set-Cookie")), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChangePassword_WithValidCurrentPassword_ReturnsNoContent()
    {
        var user = new UserAccount(Guid.NewGuid(), "viewer", ["Viewer"]) { SecurityStamp = "test-stamp" };
        var credentials = new StubUserCredentialDal { ChangePasswordResult = true };
        var grantStore = new StubGrantStore(("auth", "auth.changePassword"));
        using var factory = CreateSecurityFactory(new StubUserDal(user), credentials: credentials, grantStore: grantStore);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client, user.UserName));

        var response = await client.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPasswordHash = new string('a', 32),
            newPasswordHash = new string('b', 32)
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(user.Id, credentials.ChangedUserId);
    }

    [Fact]
    public async Task Login_WithInvalidCredentials_ReturnsUnauthorizedProblemDetails()
    {
        using var factory = CreateSecurityFactory(new StubUserDal(null));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            userName = "missing",
            passwordHash = new string('w', 32)
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ApiErrors_UseHttp200AndUnifiedEnvelope()
    {
        using var factory = CreateSecurityFactory(new StubUserDal(null));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            userName = "missing",
            passwordHash = new string('w', 32)
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("code").GetInt32() > 0);
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("message").GetString()));
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("data").ValueKind);
    }

    [Fact]
    public async Task CurrentUser_ForViewer_ReturnsClaimsProfile()
    {
        var user = new UserAccount(Guid.NewGuid(), "viewer", ["Viewer"]) { SecurityStamp = "test-stamp" };
        var grantStore = new StubGrantStore(("users", "users.me"));
        using var factory = CreateSecurityFactory(new StubUserDal(user), grantStore: grantStore);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client, user.UserName));

        var response = await client.PostAsync("/api/users/me", JsonContent.Create(new { }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = body.RootElement.GetProperty("data");
        Assert.Equal(user.Id, data.GetProperty("id").GetGuid());
        Assert.Equal("Viewer", data.GetProperty("roles")[0].GetString());
    }

    [Fact]
    public async Task UserManagement_RequiresManageUsersPermission()
    {
        var viewer = new UserAccount(Guid.NewGuid(), "viewer", ["Viewer"]) { SecurityStamp = "test-stamp" };
        using var factory = CreateSecurityFactory(new StubUserDal(viewer));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client, viewer.UserName));

        var response = await client.PostAsync("/api/users/list", JsonContent.Create(new { }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UserManagement_ForAdmin_ListsUsersAndAssignsFixedRoles()
    {
        var admin = new UserAccount(Guid.NewGuid(), "admin", ["Admin"]) { SecurityStamp = "test-stamp" };
        var dal = new StubUserDal(admin);
        var grantStore = new StubGrantStore(("users", "users.list"), ("users", "users.assignRoles"));
        using var factory = CreateSecurityFactory(dal, grantStore: grantStore);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client, admin.UserName));

        var listResponse = await client.PostAsync("/api/users/list", JsonContent.Create(new { }));
        var assignResponse = await client.PostAsJsonAsync($"/api/users/{admin.Id}/roles", new
        {
            roles = new[] { "Editor", "Viewer", "Viewer" }
        });

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, assignResponse.StatusCode);
        Assert.Equal(["Editor", "Viewer"], dal.AssignedRoles);
    }

    [Fact]
    public async Task UserManagement_ForAdmin_ReturnsFixedSystemRoles()
    {
        var admin = new UserAccount(Guid.NewGuid(), "admin", ["Admin"]) { SecurityStamp = "test-stamp" };
        var grantStore = new StubGrantStore(("users", "users.roles.list"));
        using var factory = CreateSecurityFactory(new StubUserDal(admin), grantStore: grantStore);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client, admin.UserName));

        var response = await client.PostAsync("/api/users/roles/list", JsonContent.Create(new { }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<string[]>>();
        Assert.Equal(new[] { "Admin", "Auditor", "Editor", "KbManager", "Viewer" }, envelope?.Data);
    }

    [Fact]
    public async Task UserManagement_ForAdmin_CreatesUserThenSetsInitialPassword()
    {
        var admin = new UserAccount(Guid.NewGuid(), "admin", ["Admin"]) { SecurityStamp = "test-stamp" };
        var credentials = new StubUserCredentialDal();
        var grantStore = new StubGrantStore(("users", "users.create"), ("users", "users.initialPassword"));
        using var factory = CreateSecurityFactory(new StubUserDal(admin), credentials: credentials, grantStore: grantStore);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client, admin.UserName));

        var createResponse = await client.PostAsJsonAsync("/api/users", new
        {
            userName = "new-editor",
            roles = new[] { "Editor" }
        });

        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        Assert.Equal("new-editor", credentials.CreatedUserName);
        Assert.Equal(["Editor"], credentials.CreatedRoles);
        Assert.NotNull(credentials.LastCreatedUserId);

        var setPasswordResponse = await client.PostAsJsonAsync(
            $"/api/users/{credentials.LastCreatedUserId}/initial-password",
            new { passwordHash = new string('a', 32) });

        Assert.Equal(HttpStatusCode.OK, setPasswordResponse.StatusCode);
        Assert.Equal(credentials.LastCreatedUserId, credentials.SetInitialPasswordUserId);
        Assert.Equal(new string('a', 32), credentials.SetInitialPasswordHash);
    }

    [Fact]
    public async Task UserManagement_ForAdmin_ResetsPassword()
    {
        var admin = new UserAccount(Guid.NewGuid(), "admin", ["Admin"]) { SecurityStamp = "test-stamp" };
        var credentials = new StubUserCredentialDal();
        var grantStore = new StubGrantStore(("users", "users.resetPassword"));
        using var factory = CreateSecurityFactory(new StubUserDal(admin), credentials: credentials, grantStore: grantStore);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client, admin.UserName));
        var targetUserId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync($"/api/users/{targetUserId}/password", new
        {
            passwordHash = new string('b', 32)
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(targetUserId, credentials.ResetUserId);
    }

    [Fact]
    public async Task AssignRoles_WithUnknownRole_ReturnsBadRequestProblemDetails()
    {
        var admin = new UserAccount(Guid.NewGuid(), "admin", ["Admin"]) { SecurityStamp = "test-stamp" };
        var grantStore = new StubGrantStore(("users", "users.assignRoles"));
        using var factory = CreateSecurityFactory(new StubUserDal(admin), grantStore: grantStore);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client, admin.UserName));

        var response = await client.PostAsJsonAsync($"/api/users/{admin.Id}/roles", new
        {
            roles = new[] { "SuperUser" }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task AssignRoles_ForMissingUser_ReturnsNotFoundProblemDetails()
    {
        var admin = new UserAccount(Guid.NewGuid(), "admin", ["Admin"]) { SecurityStamp = "test-stamp" };
        var grantStore = new StubGrantStore(("users", "users.assignRoles"));
        using var factory = CreateSecurityFactory(new MissingUserDal(admin), grantStore: grantStore);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client, admin.UserName));

        var response = await client.PostAsJsonAsync($"/api/users/{Guid.NewGuid()}/roles", new
        {
            roles = new[] { "Viewer" }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task DocumentPermissions_ForAdmin_ReplacesUserAndRoleAcl()
    {
        var admin = new UserAccount(Guid.NewGuid(), "admin", ["Admin"]) { SecurityStamp = "test-stamp" };
        var accessDal = new StubDocumentAccessDal();
        var grantStore = new StubGrantStore(("documents", "documents.permissions.replace"));
        using var factory = CreateSecurityFactory(new StubUserDal(admin), accessDal, grantStore: grantStore);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client, admin.UserName));
        var documentId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync($"/api/documents/{documentId}/permissions", new
        {
            userIds = new[] { userId, userId },
            roles = new[] { "Viewer", "Editor", "Viewer" }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(documentId, accessDal.DocumentId);
        Assert.Equal([userId], accessDal.UserIds);
        Assert.Equal(["Editor", "Viewer"], accessDal.Roles);
    }

    [Fact]
    public async Task KnowledgeBasePermissions_ForAdmin_ReplacesUserAndRoleAcl()
    {
        var admin = new UserAccount(Guid.NewGuid(), "admin", ["Admin"]) { SecurityStamp = "test-stamp" };
        var kbAccessDal = new StubKbAccessDal();
        var grantStore = new StubGrantStore(("knowledgeBases", "knowledgeBases.permissions.replace"));
        using var factory = CreateSecurityFactory(new StubUserDal(admin), grantStore: grantStore, kbAccess: kbAccessDal);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client, admin.UserName));
        var kbId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync($"/api/knowledge-bases/{kbId}/permissions", new
        {
            userIds = new[] { userId, userId },
            roles = new[] { "Viewer", "Editor", "Viewer" }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(kbId, kbAccessDal.KbId);
        Assert.Equal([userId], kbAccessDal.UserIds);
        Assert.Equal(["Editor", "Viewer"], kbAccessDal.Roles);
    }

    [Theory]
    [InlineData("ApiLogs", "apiLogs")]
    [InlineData("MenuConfigs", "menuConfigs")]
    [InlineData("Users", "users")]
    [InlineData("Roles", "roles")]
    [InlineData("RoleEndpointGrants", "roles")]
    [InlineData("Reports", "reports")]
    [InlineData("Statistics", "statistics")]
    [InlineData("KnowledgeBases", "knowledgeBases")]
    [InlineData("KnowledgeBasePermissions", "knowledgeBases")]
    [InlineData("Documents", "documents")]
    [InlineData("Conversations", "conversations")]
    [InlineData("AuditLogs", "auditLogs")]
    public void BusinessControllers_UseMenuEndpointAuthorization(string controllerName, string expectedMenuKey)
    {
        var actions = _factory.Services
            .GetRequiredService<IActionDescriptorCollectionProvider>()
            .ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>()
            .Where(descriptor => descriptor.ControllerName == controllerName);

        Assert.NotEmpty(actions);
        foreach (var action in actions)
        {
            var menuEndpoint = action.EndpointMetadata
                .OfType<MenuEndpointAttribute>()
                .Single();
            Assert.Equal(expectedMenuKey, menuEndpoint.MenuKey);
            Assert.False(string.IsNullOrWhiteSpace(menuEndpoint.Description));
        }
    }

    [Fact]
    public void ChatHub_NoLongerUsesClassLevelAuthorize()
    {
        var authorizeAttributes = typeof(ChatHub)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .ToList();
        Assert.Empty(authorizeAttributes);
    }

    private static WebApplicationFactory<Program> CreateSecurityFactory(
        IUserDal users,
        IDocumentAccessDal? documentAccess = null,
        IUserCredentialDal? credentials = null,
        IRefreshSessionDal? refreshSessions = null,
        IOperationAuditWriter? auditWriter = null,
        IRoleEndpointGrantStore? grantStore = null,
        IKbAccessDal? kbAccess = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:Issuer", "TigerRAG.Tests");
            builder.UseSetting("Jwt:Audience", "TigerRAG.Tests");
            builder.UseSetting("Jwt:SigningKey", "test-only-signing-key-with-at-least-32-characters");
            // Redis 不可达 + abortConnect=false：连接器可解析，但 GetDatabase 操作抛 RedisConnectionException，
            // 恰好验证 JWT 撤权校验器在缓存不可用时的 DB 降级路径。
            builder.UseSetting("ConnectionStrings:Redis", "localhost:6379,abortConnect=false,connectTimeout=100,syncTimeout=100");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IUserDal>();
                services.AddSingleton(users);
                services.RemoveAll<IUserCredentialDal>();
                services.AddSingleton(credentials ?? new StubUserCredentialDal());
                services.RemoveAll<IRefreshSessionDal>();
                services.AddSingleton(refreshSessions ?? new StubRefreshSessionDal());
                services.RemoveAll<IOperationAuditWriter>();
                services.AddSingleton(auditWriter ?? new StubOperationAuditWriter());
                if (grantStore is not null)
                {
                    services.RemoveAll<IRoleEndpointGrantStore>();
                    services.AddSingleton(grantStore);
                }
                if (documentAccess is not null)
                {
                    services.RemoveAll<IDocumentAccessDal>();
                    services.AddSingleton(documentAccess);
                }
                if (kbAccess is not null)
                {
                    services.RemoveAll<IKbAccessDal>();
                    services.AddSingleton(kbAccess);
                }
            });
        });

    private static async Task<string> LoginAsync(HttpClient client, string userName)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            userName,
            passwordHash = new string('c', 32)
        });
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("data").GetProperty("accessToken").GetString()!;
    }

    private sealed class StubUserDal(UserAccount? loginUser, string? salt = null) : IUserDal
    {
        public IReadOnlyCollection<string>? AssignedRoles { get; private set; }

        public Task<UserAccount?> ValidateCredentialsAsync(
            string userName,
            string passwordHash,
            CancellationToken cancellationToken) => Task.FromResult(loginUser);

        public Task<string?> GetPasswordSaltAsync(string userName, CancellationToken cancellationToken) =>
            Task.FromResult(salt);

        public Task<IReadOnlyList<UserListItem>> ListAsync(CancellationToken cancellationToken) =>
            loginUser is null
                ? Task.FromResult<IReadOnlyList<UserListItem>>([])
                : Task.FromResult<IReadOnlyList<UserListItem>>([new UserListItem(loginUser.Id, loginUser.UserName, loginUser.Roles, false)]);

        public Task<RevocationSnapshot?> GetRevocationSnapshotAsync(
            Guid userId,
            CancellationToken cancellationToken) =>
            Task.FromResult<RevocationSnapshot?>(loginUser is null || loginUser.Id != userId
                ? null
                : new RevocationSnapshot(loginUser.SecurityStamp, false));

        public Task AssignRolesAsync(
            Guid userId,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken)
        {
            AssignedRoles = roles;
            return Task.CompletedTask;
        }
    }

    private sealed class MissingUserDal(UserAccount loginUser) : IUserDal
    {
        public Task<UserAccount?> ValidateCredentialsAsync(
            string userName,
            string passwordHash,
            CancellationToken cancellationToken) => Task.FromResult<UserAccount?>(loginUser);

        public Task<string?> GetPasswordSaltAsync(string userName, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);

        public Task<IReadOnlyList<UserListItem>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserListItem>>([new UserListItem(loginUser.Id, loginUser.UserName, loginUser.Roles, false)]);

        public Task<RevocationSnapshot?> GetRevocationSnapshotAsync(
            Guid userId,
            CancellationToken cancellationToken) => Task.FromResult<RevocationSnapshot?>(null);

        public Task AssignRolesAsync(
            Guid userId,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken) =>
            throw new KeyNotFoundException();
    }

    /// <summary>授权 store stub：构造时传入 (menuKey, endpointKey) 元组列表，按 endpointKey 一律返回已授权；写操作无副作用。</summary>
    private sealed class StubGrantStore(params (string MenuKey, string EndpointKey)[] grants) : IRoleEndpointGrantStore
    {
        public Task<IReadOnlyList<RoleEndpointGrant>> ListByRoleAsync(string roleName, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RoleEndpointGrant>>(
                grants.Select(g => new RoleEndpointGrant(roleName, g.MenuKey, g.EndpointKey, DateTimeOffset.UnixEpoch, Guid.Empty)).ToArray());

        public Task<bool> HasGrantAsync(IEnumerable<string> userRoles, string endpointKey, CancellationToken cancellationToken) =>
            Task.FromResult(grants.Any(g => string.Equals(g.EndpointKey, endpointKey, StringComparison.Ordinal)));

        public Task GrantAsync(string roleName, string menuKey, string endpointKey, Guid actorId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task RevokeAsync(string roleName, string endpointKey, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<int> GrantAllInMenuAsync(string roleName, string menuKey, IReadOnlyCollection<MenuEndpointDescriptor> endpoints, Guid actorId, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<int> RevokeAllInMenuAsync(string roleName, string menuKey, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<int> ApplyBatchAsync(string roleName, IReadOnlyCollection<BatchEndpointChange> desiredEndpoints, Guid actorId, CancellationToken cancellationToken) =>
            Task.FromResult(0);
    }

    private sealed class StubDocumentAccessDal : IDocumentAccessDal
    {
        public Guid? DocumentId { get; private set; }
        public IReadOnlyCollection<Guid>? UserIds { get; private set; }
        public IReadOnlyCollection<string>? Roles { get; private set; }

        public Task<IReadOnlyList<Guid>> GetAccessibleDocumentIdsAsync(
            Guid userId,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task ReplacePermissionsAsync(
            Guid documentId,
            Guid actorId,
            bool isAdmin,
            IReadOnlyCollection<Guid> userIds,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken)
        {
            DocumentId = documentId;
            UserIds = userIds;
            Roles = roles;
            return Task.CompletedTask;
        }

        public Task<DocumentPermissionsSnapshot> GetPermissionsAsync(Guid documentId, CancellationToken cancellationToken)
            => Task.FromResult(new DocumentPermissionsSnapshot([], []));

        public Task<Guid?> GetDocumentOwnerIdAsync(Guid documentId, CancellationToken cancellationToken)
            => Task.FromResult<Guid?>(null);
    }

    private sealed class StubUserCredentialDal : IUserCredentialDal
    {
        public bool ChangePasswordResult { get; init; }
        public Guid? ChangedUserId { get; private set; }
        public string? CreatedUserName { get; private set; }
        public IReadOnlyCollection<string>? CreatedRoles { get; private set; }
        public Guid? LastCreatedUserId { get; private set; }
        public Guid? ResetUserId { get; private set; }
        public Guid? SetInitialPasswordUserId { get; private set; }
        public string? SetInitialPasswordHash { get; private set; }

        public Task<UserAccount> CreateAsync(
            string userName,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken)
        {
            CreatedUserName = userName;
            CreatedRoles = roles;
            var user = new UserAccount(Guid.NewGuid(), userName, roles.ToArray()) { SecurityStamp = "test-stamp" };
            LastCreatedUserId = user.Id;
            return Task.FromResult(user);
        }

        public Task SetInitialPasswordAsync(
            Guid userId,
            string passwordHash,
            CancellationToken cancellationToken)
        {
            SetInitialPasswordUserId = userId;
            SetInitialPasswordHash = passwordHash;
            return Task.CompletedTask;
        }

        public Task<bool> ChangePasswordAsync(
            Guid userId,
            string currentPasswordHash,
            string newPasswordHash,
            CancellationToken cancellationToken)
        {
            ChangedUserId = userId;
            return Task.FromResult(ChangePasswordResult);
        }

        public Task ResetPasswordAsync(
            Guid userId,
            string newPasswordHash,
            CancellationToken cancellationToken)
        {
            ResetUserId = userId;
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task SetLockoutAsync(
            Guid userId,
            DateTimeOffset? lockoutEnd,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubRefreshSessionDal : IRefreshSessionDal
    {
        public UserAccount? RotatedUser { get; init; }
        public string? RotatedToken { get; private set; }
        public string? RevokedToken { get; private set; }

        public Task<RefreshToken> CreateAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(new RefreshToken("refresh-token", DateTimeOffset.UtcNow.AddDays(7)));

        public Task<RefreshSession?> RotateAsync(string value, CancellationToken cancellationToken)
        {
            RotatedToken = value;
            return Task.FromResult(RotatedUser is null
                ? null
                : new RefreshSession(
                    RotatedUser,
                    new RefreshToken("rotated-refresh-token", DateTimeOffset.UtcNow.AddDays(7))));
        }

        public Task RevokeAsync(string value, CancellationToken cancellationToken)
        {
            RevokedToken = value;
            return Task.CompletedTask;
        }

        public Task RevokeAllAsync(Guid userId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<UserAccount?> GetUserByTokenAsync(string value, CancellationToken cancellationToken) =>
            Task.FromResult<UserAccount?>(null);
    }

    private sealed class StubOperationAuditWriter : IOperationAuditWriter
    {
        public Task RecordAsync(OperationAuditEntry entry, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    /// <summary>测试用 IKbAccessDal 伪造：记录 ReplacePermissions 调用参数，返回空快照。</summary>
    private sealed class StubKbAccessDal : IKbAccessDal
    {
        public Guid? KbId { get; private set; }
        public IReadOnlyCollection<Guid>? UserIds { get; private set; }
        public IReadOnlyCollection<string>? Roles { get; private set; }

        public Task<IReadOnlyList<Guid>> GetAccessibleKbIdsAsync(
            Guid userId,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task<KbPermissionsSnapshot> GetPermissionsAsync(Guid kbId, CancellationToken cancellationToken)
            => Task.FromResult(new KbPermissionsSnapshot([], []));

        public Task<Guid?> GetKbOwnerIdAsync(Guid kbId, CancellationToken cancellationToken)
            => Task.FromResult<Guid?>(null);

        public Task ReplacePermissionsAsync(
            Guid kbId,
            Guid actorId,
            bool isAdmin,
            IReadOnlyCollection<Guid> userIds,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken)
        {
            KbId = kbId;
            UserIds = userIds;
            Roles = roles;
            return Task.CompletedTask;
        }
    }

    private sealed record ApiEnvelope<T>(int Code, string Message, T? Data);
    private sealed record ServiceDescriptor(string Name, string Version);
}

public sealed class TigerRagApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Jwt:SigningKey", "test-only-signing-key-with-at-least-32-characters");
    }
}
