using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TigerRAG.Application.Auth;
using TigerRAG.Application.OperationAudit;
using TigerRAG.Application.Shared;
using TigerRAG.Application.Users;
using TigerRAG.Infrastructure.Dal;

namespace TigerRAG.IntegrationTests.Api.Auth;

/// <summary>
/// 端到端验证：管理员调用重置密码接口时，客户端提交的密码哈希必须符合
/// <c>32 位小写 hex</c> 格式；任何不合规输入都被业务码 <c>Validation</c> 拒绝。
/// </summary>
public sealed class PasswordStrengthTests
{
    [Fact]
    public async Task ResetPassword_WithTooShortHash_ReturnsValidation()
    {
        using var factory = CreateFactory();
        using var client = await LoginAsAdminAsync(factory);

        var response = await client.PostAsJsonAsync($"/api/users/{Guid.NewGuid()}/password", new
        {
            passwordHash = "too-short"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(40000, await ReadCodeAsync(response));
    }

    [Fact]
    public async Task ResetPassword_WithUpperHex32_ReturnsValidation()
    {
        using var factory = CreateFactory();
        using var client = await LoginAsAdminAsync(factory);

        var response = await client.PostAsJsonAsync($"/api/users/{Guid.NewGuid()}/password", new
        {
            passwordHash = new string('A', 32)
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(40000, await ReadCodeAsync(response));
    }

    [Fact]
    public async Task ResetPassword_WithNonHexChar_ReturnsValidation()
    {
        using var factory = CreateFactory();
        using var client = await LoginAsAdminAsync(factory);

        var response = await client.PostAsJsonAsync($"/api/users/{Guid.NewGuid()}/password", new
        {
            passwordHash = "g" + new string('a', 31)
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(40000, await ReadCodeAsync(response));
    }

    [Fact]
    public async Task ResetPassword_WithValidLowerHex32_ReturnsSuccess()
    {
        using var factory = CreateFactory();
        using var client = await LoginAsAdminAsync(factory);

        var response = await client.PostAsJsonAsync($"/api/users/{Guid.NewGuid()}/password", new
        {
            passwordHash = new string('a', 32)
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, await ReadCodeAsync(response));
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:Issuer", "TigerRAG.Tests");
            builder.UseSetting("Jwt:Audience", "TigerRAG.Tests");
            builder.UseSetting("Jwt:SigningKey", "test-only-signing-key-with-at-least-32-characters");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IUserDal>();
                services.AddSingleton<IUserDal>(new AdminUserDal());
                services.RemoveAll<IUserCredentialDal>();
                services.AddSingleton<IUserCredentialDal>(new RecordingCredentialDal());
                services.RemoveAll<IRefreshSessionDal>();
                services.AddSingleton<IRefreshSessionDal>(new NoopRefreshSessionDal());
                services.RemoveAll<IUserSecurityStampRotator>();
                services.AddSingleton<IUserSecurityStampRotator>(new NoopSecurityStampRotator());
                services.RemoveAll<IOperationAuditWriter>();
                services.AddSingleton<IOperationAuditWriter>(new StubOperationAuditWriter());
                // 给 Admin 用户授予 users.resetPassword 权限，使能访问重置密码端点。
                services.RemoveAll<IRoleEndpointGrantStore>();
                services.AddSingleton<IRoleEndpointGrantStore>(new AllowAllGrantStore());
            });
        });

    private static async Task<HttpClient> LoginAsAdminAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            userName = "admin",
            passwordHash = new string('c', 32)
        });
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var token = body.RootElement.GetProperty("data").GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<int> ReadCodeAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("code").GetInt32();
    }

    private sealed class AdminUserDal : IUserDal
    {
        public static readonly UserAccount Admin =
            new(Guid.NewGuid(), "admin", ["Admin"]) { SecurityStamp = "test-stamp" };

        public Task<UserAccount?> ValidateCredentialsAsync(string userName, string passwordHash, CancellationToken cancellationToken)
            => Task.FromResult<UserAccount?>(Admin);
        public Task<string?> GetPasswordSaltAsync(string userName, CancellationToken cancellationToken)
            => Task.FromResult<string?>("test-salt");
        public Task<IReadOnlyList<UserListItem>> ListAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<UserListItem>>([new UserListItem(Admin.Id, Admin.UserName, Admin.Roles, false)]);
        public Task AssignRolesAsync(Guid userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken)
            => Task.CompletedTask;
        public Task<RevocationSnapshot?> GetRevocationSnapshotAsync(
            Guid userId,
            CancellationToken cancellationToken) => Task.FromResult<RevocationSnapshot?>(
                userId == Admin.Id ? new RevocationSnapshot("test-stamp", false) : null);
    }

    private sealed class RecordingCredentialDal : IUserCredentialDal
    {
        public Guid? LastResetUserId { get; private set; }
        public string? LastResetHash { get; private set; }

        public Task<UserAccount> CreateAsync(string userName, IReadOnlyCollection<string> roles, CancellationToken cancellationToken)
            => Task.FromResult(new UserAccount(Guid.NewGuid(), userName, [.. roles]));
        public Task SetInitialPasswordAsync(Guid userId, string passwordHash, CancellationToken cancellationToken)
            => Task.CompletedTask;
        public Task<bool> ChangePasswordAsync(Guid userId, string currentPasswordHash, string newPasswordHash, CancellationToken cancellationToken)
            => Task.FromResult(true);
        public Task ResetPasswordAsync(Guid userId, string newPasswordHash, CancellationToken cancellationToken)
        {
            LastResetUserId = userId;
            LastResetHash = newPasswordHash;
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task SetLockoutAsync(
            Guid userId,
            DateTimeOffset? lockoutEnd,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NoopRefreshSessionDal : IRefreshSessionDal
    {
        public Task<RefreshToken> CreateAsync(Guid userId, CancellationToken cancellationToken)
            => Task.FromResult(new RefreshToken("stub-refresh", DateTimeOffset.UtcNow.AddDays(7)));
        public Task<RefreshSession?> RotateAsync(string value, CancellationToken cancellationToken)
            => Task.FromResult<RefreshSession?>(null);
        public Task RevokeAsync(string value, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RevokeAllAsync(Guid userId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<UserAccount?> GetUserByTokenAsync(string value, CancellationToken cancellationToken) =>
            Task.FromResult<UserAccount?>(null);
    }

    private sealed class NoopSecurityStampRotator : IUserSecurityStampRotator
    {
        public Task RotateAsync(Guid userId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task InvalidateAsync(Guid userId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>
    /// 全授权 store stub：所有 endpoint 一律返回已授权，用于不需要精细授权控制的测试。
    /// </summary>
    private sealed class AllowAllGrantStore : IRoleEndpointGrantStore
    {
        public Task<IReadOnlyList<RoleEndpointGrant>> ListByRoleAsync(string roleName, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RoleEndpointGrant>>([]);

        public Task<bool> HasGrantAsync(IEnumerable<string> userRoles, string endpointKey, CancellationToken cancellationToken) =>
            Task.FromResult(true);

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
    private sealed class StubOperationAuditWriter : IOperationAuditWriter
    {
        public Task RecordAsync(OperationAuditEntry entry, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}