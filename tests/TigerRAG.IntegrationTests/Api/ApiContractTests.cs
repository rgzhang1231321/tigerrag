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
using TigerRAG.Api;
using TigerRAG.Api.Hubs;
using TigerRAG.Application.Security;

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
    [InlineData("GET", "/api/users/me")]
    [InlineData("GET", "/api/knowledge-bases")]
    [InlineData("GET", "/api/documents/00000000-0000-0000-0000-000000000001")]
    [InlineData("POST", "/api/conversations")]
    [InlineData("GET", "/api/audit-logs")]
    public async Task SecuredModuleContract_RejectsAnonymousRequests(string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);

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
        var user = new UserAccount(Guid.NewGuid(), "editor", [SystemRoles.Editor]);
        using var factory = CreateSecurityFactory(new StubUserDal(user));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            userName = "editor",
            password = "correct-password"
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
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refresh_WithValidCookie_RotatesSessionAndReturnsAccessToken()
    {
        var user = new UserAccount(Guid.NewGuid(), "viewer", [SystemRoles.Viewer]);
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
        var user = new UserAccount(Guid.NewGuid(), "viewer", [SystemRoles.Viewer]);
        var credentials = new StubUserCredentialDal { ChangePasswordResult = true };
        using var factory = CreateSecurityFactory(new StubUserDal(user), credentials: credentials);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client, user.UserName));

        var response = await client.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = "current-password",
            newPassword = "new-password-123"
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
            password = "wrong-password"
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
            password = "wrong-password"
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
        var user = new UserAccount(Guid.NewGuid(), "viewer", [SystemRoles.Viewer]);
        using var factory = CreateSecurityFactory(new StubUserDal(user));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client, user.UserName));

        var response = await client.GetAsync("/api/users/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = body.RootElement.GetProperty("data");
        Assert.Equal(user.Id, data.GetProperty("id").GetGuid());
        Assert.Equal(SystemRoles.Viewer, data.GetProperty("roles")[0].GetString());
    }

    [Fact]
    public async Task UserManagement_RequiresManageUsersPermission()
    {
        var viewer = new UserAccount(Guid.NewGuid(), "viewer", [SystemRoles.Viewer]);
        using var factory = CreateSecurityFactory(new StubUserDal(viewer));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client, viewer.UserName));

        var response = await client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UserManagement_ForAdmin_ListsUsersAndAssignsFixedRoles()
    {
        var admin = new UserAccount(Guid.NewGuid(), "admin", [SystemRoles.Admin]);
        var dal = new StubUserDal(admin);
        using var factory = CreateSecurityFactory(dal);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client, admin.UserName));

        var listResponse = await client.GetAsync("/api/users");
        var assignResponse = await client.PutAsJsonAsync($"/api/users/{admin.Id}/roles", new
        {
            roles = new[] { SystemRoles.Editor, SystemRoles.Viewer, SystemRoles.Viewer }
        });

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, assignResponse.StatusCode);
        Assert.Equal([SystemRoles.Editor, SystemRoles.Viewer], dal.AssignedRoles);
    }

    [Fact]
    public async Task UserManagement_ForAdmin_ReturnsFixedSystemRoles()
    {
        var admin = new UserAccount(Guid.NewGuid(), "admin", [SystemRoles.Admin]);
        using var factory = CreateSecurityFactory(new StubUserDal(admin));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client, admin.UserName));

        var response = await client.GetAsync("/api/users/roles");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<string[]>>();
        Assert.Equal(SystemRoles.All.OrderBy(role => role, StringComparer.Ordinal), envelope?.Data);
    }

    [Fact]
    public async Task UserManagement_ForAdmin_CreatesUser()
    {
        var admin = new UserAccount(Guid.NewGuid(), "admin", [SystemRoles.Admin]);
        var credentials = new StubUserCredentialDal();
        using var factory = CreateSecurityFactory(new StubUserDal(admin), credentials: credentials);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client, admin.UserName));

        var response = await client.PostAsJsonAsync("/api/users", new
        {
            userName = "new-editor",
            password = "initial-password",
            roles = new[] { SystemRoles.Editor }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("new-editor", credentials.CreatedUserName);
        Assert.Equal([SystemRoles.Editor], credentials.CreatedRoles);
    }

    [Fact]
    public async Task UserManagement_ForAdmin_ResetsPassword()
    {
        var admin = new UserAccount(Guid.NewGuid(), "admin", [SystemRoles.Admin]);
        var credentials = new StubUserCredentialDal();
        using var factory = CreateSecurityFactory(new StubUserDal(admin), credentials: credentials);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client, admin.UserName));
        var targetUserId = Guid.NewGuid();

        var response = await client.PutAsJsonAsync($"/api/users/{targetUserId}/password", new
        {
            newPassword = "temporary-password"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(targetUserId, credentials.ResetUserId);
    }

    [Fact]
    public async Task AssignRoles_WithUnknownRole_ReturnsBadRequestProblemDetails()
    {
        var admin = new UserAccount(Guid.NewGuid(), "admin", [SystemRoles.Admin]);
        using var factory = CreateSecurityFactory(new StubUserDal(admin));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client, admin.UserName));

        var response = await client.PutAsJsonAsync($"/api/users/{admin.Id}/roles", new
        {
            roles = new[] { "SuperUser" }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task AssignRoles_ForMissingUser_ReturnsNotFoundProblemDetails()
    {
        var admin = new UserAccount(Guid.NewGuid(), "admin", [SystemRoles.Admin]);
        using var factory = CreateSecurityFactory(new MissingUserDal(admin));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client, admin.UserName));

        var response = await client.PutAsJsonAsync($"/api/users/{Guid.NewGuid()}/roles", new
        {
            roles = new[] { SystemRoles.Viewer }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task DocumentPermissions_ForAdmin_ReplacesUserAndRoleAcl()
    {
        var admin = new UserAccount(Guid.NewGuid(), "admin", [SystemRoles.Admin]);
        var accessDal = new StubDocumentAccessDal();
        using var factory = CreateSecurityFactory(new StubUserDal(admin), accessDal);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client, admin.UserName));
        var documentId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var response = await client.PutAsJsonAsync($"/api/documents/{documentId}/permissions", new
        {
            userIds = new[] { userId, userId },
            roles = new[] { SystemRoles.Viewer, SystemRoles.Editor, SystemRoles.Viewer }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(documentId, accessDal.DocumentId);
        Assert.Equal([userId], accessDal.UserIds);
        Assert.Equal([SystemRoles.Editor, SystemRoles.Viewer], accessDal.Roles);
    }

    [Theory]
    [InlineData("KnowledgeBases", SystemPermissions.ManageKnowledgeBases)]
    [InlineData("Documents", SystemPermissions.ManageDocuments)]
    [InlineData("Conversations", SystemPermissions.UseChat)]
    [InlineData("AuditLogs", SystemPermissions.ReadAudit)]
    public void BusinessControllers_UsePermissionPolicies(string controllerName, string permission)
    {
        var action = _factory.Services
            .GetRequiredService<IActionDescriptorCollectionProvider>()
            .ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>()
            .First(descriptor => descriptor.ControllerName == controllerName);

        var policies = action.EndpointMetadata
            .OfType<IAuthorizeData>()
            .Select(metadata => metadata.Policy);

        Assert.Contains(permission, policies);
    }

    [Fact]
    public void ChatHub_UsesChatPermissionPolicy()
    {
        var authorization = Assert.Single(
            typeof(ChatHub).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
                .Cast<AuthorizeAttribute>());

        Assert.Equal(SystemPermissions.UseChat, authorization.Policy);
        Assert.Null(authorization.Roles);
    }

    private static WebApplicationFactory<Program> CreateSecurityFactory(
        IUserDal users,
        IDocumentAccessDal? documentAccess = null,
        IUserCredentialDal? credentials = null,
        IRefreshSessionDal? refreshSessions = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:Issuer", "TigerRAG.Tests");
            builder.UseSetting("Jwt:Audience", "TigerRAG.Tests");
            builder.UseSetting("Jwt:SigningKey", "test-only-signing-key-with-at-least-32-characters");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IUserDal>();
                services.AddSingleton(users);
                services.RemoveAll<IUserCredentialDal>();
                services.AddSingleton(credentials ?? new StubUserCredentialDal());
                services.RemoveAll<IRefreshSessionDal>();
                services.AddSingleton(refreshSessions ?? new StubRefreshSessionDal());
                if (documentAccess is not null)
                {
                    services.RemoveAll<IDocumentAccessDal>();
                    services.AddSingleton(documentAccess);
                }
            });
        });

    private static async Task<string> LoginAsync(HttpClient client, string userName)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            userName,
            password = "correct-password"
        });
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("data").GetProperty("accessToken").GetString()!;
    }

    private sealed class StubUserDal(UserAccount? loginUser) : IUserDal
    {
        public IReadOnlyCollection<string>? AssignedRoles { get; private set; }

        public Task<UserAccount?> ValidateCredentialsAsync(
            string userName,
            string password,
            CancellationToken cancellationToken) => Task.FromResult(loginUser);

        public Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserAccount>>(loginUser is null ? [] : [loginUser]);

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
            string password,
            CancellationToken cancellationToken) => Task.FromResult<UserAccount?>(loginUser);

        public Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserAccount>>([loginUser]);

        public Task AssignRolesAsync(
            Guid userId,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken) =>
            throw new KeyNotFoundException();
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
    }

    private sealed class StubUserCredentialDal : IUserCredentialDal
    {
        public bool ChangePasswordResult { get; init; }
        public Guid? ChangedUserId { get; private set; }
        public string? CreatedUserName { get; private set; }
        public IReadOnlyCollection<string>? CreatedRoles { get; private set; }
        public Guid? ResetUserId { get; private set; }

        public Task<UserAccount> CreateAsync(
            string userName,
            string password,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken)
        {
            CreatedUserName = userName;
            CreatedRoles = roles;
            return Task.FromResult(new UserAccount(Guid.NewGuid(), userName, roles.ToArray()));
        }

        public Task<bool> ChangePasswordAsync(
            Guid userId,
            string currentPassword,
            string newPassword,
            CancellationToken cancellationToken)
        {
            ChangedUserId = userId;
            return Task.FromResult(ChangePasswordResult);
        }

        public Task ResetPasswordAsync(
            Guid userId,
            string newPassword,
            CancellationToken cancellationToken)
        {
            ResetUserId = userId;
            return Task.CompletedTask;
        }
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
