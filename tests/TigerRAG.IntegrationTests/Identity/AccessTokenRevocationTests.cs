using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TigerRAG.Api;
using TigerRAG.Application.Security;

namespace TigerRAG.IntegrationTests.Identity;

/// <summary>
/// 端到端验证：JWT 撤权（stamp 比对 + 用户存在性 + 锁口）在 OnTokenValidated 管道里生效。
/// 每个用例构造一个可写 stamp 的伪造 DAL，让管理员"轮换 stamp"——等价于生产中
/// 角色变更/改密/锁定触发的 stamp bump，再观察旧 token 是否立刻被拒。
/// </summary>
public sealed class AccessTokenRevocationTests
{
    [Fact]
    public async Task OnTokenValidated_WhenStampRotated_OldAccessTokenIsRejected()
    {
        var userId = Guid.NewGuid();
        var store = new MutableUserStore(
            new UserAccount(userId, "admin", [SystemRoles.Admin]) { SecurityStamp = "stamp-1" });
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();

        var first = await LoginAndReadTokenAsync(client);
        // 等价于生产里角色/密码/锁定触发的 stamp 轮换。
        store.RotateStamp("stamp-2");

        var response = await SendMeAsync(client, first.AccessToken);

        // 撤权在第一个请求就生效：旧 token 立刻 401，无需等过期。
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(40100, await ReadCodeAsync(response));
    }

    [Fact]
    public async Task OnTokenValidated_WhenStampMatches_AccessTokenAccepted()
    {
        var userId = Guid.NewGuid();
        var store = new MutableUserStore(
            new UserAccount(userId, "admin", [SystemRoles.Admin]) { SecurityStamp = "stamp-1" });
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();

        var first = await LoginAndReadTokenAsync(client);
        var response = await SendMeAsync(client, first.AccessToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, await ReadCodeAsync(response));
    }

    [Fact]
    public async Task OnTokenValidated_WhenUserDeleted_OldAccessTokenIsRejected()
    {
        var userId = Guid.NewGuid();
        var store = new MutableUserStore(
            new UserAccount(userId, "admin", [SystemRoles.Admin]) { SecurityStamp = "stamp-1" });
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();

        var first = await LoginAndReadTokenAsync(client);
        // 等价于生产 DeleteAsync：DB 行没了，校验器拿不到 snapshot。
        store.Remove();

        var response = await SendMeAsync(client, first.AccessToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(40100, await ReadCodeAsync(response));
    }

    [Fact]
    public async Task OnTokenValidated_WhenUserLocked_OldAccessTokenIsRejected()
    {
        var userId = Guid.NewGuid();
        var store = new MutableUserStore(
            new UserAccount(userId, "admin", [SystemRoles.Admin]) { SecurityStamp = "stamp-1" });
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();

        var first = await LoginAndReadTokenAsync(client);
        // 等价于生产 SetLockoutAsync：stamp 不变但锁口开关打开。
        store.SetLocked(true);

        var response = await SendMeAsync(client, first.AccessToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(40100, await ReadCodeAsync(response));
    }

    [Fact]
    public async Task OnTokenValidated_WhenTokenMissingSecurityStampClaim_IsRejected()
    {
        var userId = Guid.NewGuid();
        var store = new MutableUserStore(
            new UserAccount(userId, "admin", [SystemRoles.Admin]) { SecurityStamp = "stamp-1" });
        using var factory = CreateFactory(store);

        // 直接构造一个空 stamp 的 token：issuer 会写出 security_stamp="" 的 claim，
        // 校验器把空串视为缺失，强制走重登路径——P1 修复部署后旧 token 立刻 401。
        var forged = await IssueTokenForUserAsync(factory.Services, new UserAccount(userId, "admin", [SystemRoles.Admin]));

        using var client = factory.CreateClient();
        var response = await SendMeAsync(client, forged);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(40100, await ReadCodeAsync(response));
    }

    [Fact]
    public async Task OnTokenValidated_WhenStampMismatch_IsRejected()
    {
        var userId = Guid.NewGuid();
        var store = new MutableUserStore(
            new UserAccount(userId, "admin", [SystemRoles.Admin]) { SecurityStamp = "stamp-1" });
        using var factory = CreateFactory(store);

        // 用错的 stamp 签发：模拟 token 声称的 stamp 与 DB 不同（角色被改、密码被改等情形）。
        var forged = await IssueTokenForUserAsync(
            factory.Services,
            new UserAccount(userId, "admin", [SystemRoles.Admin]) { SecurityStamp = "wrong-stamp" });

        using var client = factory.CreateClient();
        var response = await SendMeAsync(client, forged);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(40100, await ReadCodeAsync(response));
    }

    private static WebApplicationFactory<Program> CreateFactory(IUserDal users) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:Issuer", "TigerRAG.Tests");
            builder.UseSetting("Jwt:Audience", "TigerRAG.Tests");
            builder.UseSetting("Jwt:SigningKey", "test-only-signing-key-with-at-least-32-characters");
            builder.UseSetting("ConnectionStrings:Redis", "localhost:6379,abortConnect=false,connectTimeout=100,syncTimeout=100");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IUserDal>();
                services.AddSingleton<IUserDal>(users);
                services.RemoveAll<IUserCredentialDal>();
                services.AddSingleton<IUserCredentialDal>(new NoopCredentialDal());
                services.RemoveAll<IRefreshSessionDal>();
                services.AddSingleton<IRefreshSessionDal>(new NoopRefreshSessionDal());
            });
        });

    private static async Task<LoginOutcome> LoginAndReadTokenAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            userName = "admin",
            passwordHash = new string('c', 32)
        });
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var token = body.RootElement.GetProperty("data").GetProperty("accessToken").GetString()!;
        return new LoginOutcome(token);
    }

    private static async Task<HttpResponseMessage> SendMeAsync(HttpClient client, string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/users/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await client.SendAsync(request);
    }

    private static async Task<string> IssueTokenForUserAsync(IServiceProvider services, UserAccount user)
    {
        // IAccessTokenIssuer 注册为 Scoped，从 root 解析会失败；必须先开 scope。
        using var scope = services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<IAccessTokenIssuer>()
            .IssueAsync(user, CancellationToken.None)).Value;
    }

    private static async Task<int> ReadCodeAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("code").GetInt32();
    }

    private sealed record LoginOutcome(string AccessToken);

    /// <summary>可写 stamp 与锁口状态的伪造 DAL；测试中按需调用 <c>RotateStamp</c>/<c>SetLocked</c>/<c>Remove</c> 模拟生产敏感动作。</summary>
    private sealed class MutableUserStore(UserAccount initial) : IUserDal
    {
        private UserAccount? _current = initial;

        public void RotateStamp(string newStamp)
        {
            if (_current is null) return;
            _current = _current with { SecurityStamp = newStamp };
        }

        public void SetLocked(bool locked)
        {
            if (_current is null) return;
            _current = _current with { };
            _lockedOverride = locked;
        }

        public void Remove() => _current = null;

        private bool _lockedOverride;

        public Task<UserAccount?> ValidateCredentialsAsync(string userName, string passwordHash, CancellationToken cancellationToken) =>
            Task.FromResult(_current);

        public Task<string?> GetPasswordSaltAsync(string userName, CancellationToken cancellationToken) =>
            Task.FromResult<string?>("test-salt");

        public Task<IReadOnlyList<UserListItem>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserListItem>>(
                _current is null ? [] : [new UserListItem(_current.Id, _current.UserName, _current.Roles, false)]);

        public Task<RevocationSnapshot?> GetRevocationSnapshotAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<RevocationSnapshot?>(_current is null || _current.Id != userId
                ? null
                : new RevocationSnapshot(_current.SecurityStamp, _lockedOverride));

        public Task AssignRolesAsync(Guid userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class NoopCredentialDal : IUserCredentialDal
    {
        public Task<UserAccount> CreateAsync(string userName, IReadOnlyCollection<string> roles, CancellationToken cancellationToken) =>
            Task.FromResult(new UserAccount(Guid.NewGuid(), userName, [.. roles]) { SecurityStamp = "noop" });

        public Task SetInitialPasswordAsync(Guid userId, string passwordHash, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> ChangePasswordAsync(Guid userId, string currentPasswordHash, string newPasswordHash, CancellationToken cancellationToken) =>
            Task.FromResult(true);
        public Task ResetPasswordAsync(Guid userId, string newPasswordHash, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> DeleteAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task SetLockoutAsync(Guid userId, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NoopRefreshSessionDal : IRefreshSessionDal
    {
        public Task<RefreshToken> CreateAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(new RefreshToken("refresh-token", DateTimeOffset.UtcNow.AddDays(7)));

        public Task<RefreshSession?> RotateAsync(string value, CancellationToken cancellationToken) =>
            Task.FromResult<RefreshSession?>(null);

        public Task RevokeAsync(string value, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RevokeAllAsync(Guid userId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
