using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TigerRAG.Application.Auth;
using TigerRAG.Application.Shared;
using TigerRAG.Application.Users;
using TigerRAG.Infrastructure.Dal;

namespace TigerRAG.IntegrationTests.Api.Auth;

/// <summary>
/// 验证 <c>X-Forwarded-Proto: https</c> 经 <see cref="ForwardedHeadersMiddleware"/> 转发后，
/// 反向代理后的刷新 Cookie 会被打上 <c>Secure</c> 标记；
/// 不配置转发头时 <see cref="HttpRequest.IsHttps"/> 仍为 false，Cookie 不带 <c>Secure</c>。
/// </summary>
public sealed class SecureCookieTests
{
    [Fact]
    public async Task Login_WithoutForwardedHeader_DoesNotSetSecureCookie()
    {
        // 不开 UseForwardedHeaders（生产代码现状）时，Cookie 永远不带 secure——锁定基线避免反向退化。
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            userName = "editor",
            passwordHash = new string('c', 32)
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.DoesNotContain("secure", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_WithForwardedProtoHttps_SetsSecureCookie()
    {
        // 注册 UseForwardedHeaders（X-Forwarded-Proto）+ 回环 KnownNetworks；发请求时附加 forwarded 头。
        using var factory = CreateFactory(allowLoopback: true);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new
            {
                userName = "editor",
                passwordHash = new string('c', 32)
            })
        };
        request.Headers.Add("X-Forwarded-Proto", "https");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
    }

    private static WebApplicationFactory<Program> CreateFactory(bool allowLoopback = false) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:Issuer", "TigerRAG.Tests");
            builder.UseSetting("Jwt:Audience", "TigerRAG.Tests");
            builder.UseSetting("Jwt:SigningKey", "test-only-signing-key-with-at-least-32-characters");
            builder.ConfigureTestServices(services =>
            {
                // 用最小 stub 让登录通过；不依赖任何外部 SDK（Postgres/Redis/Qdrant/MinIO）。
                services.RemoveAll<IUserDal>();
                services.AddSingleton<IUserDal>(new StubUserDal(
                    new UserAccount(Guid.NewGuid(), "editor", [SystemRoles.Editor]) { SecurityStamp = "test-stamp" },
                    salt: "test-salt"));
                services.RemoveAll<IUserCredentialDal>();
                services.AddSingleton<IUserCredentialDal>(new StubCredentialDal());
                services.RemoveAll<IRefreshSessionDal>();
                services.AddSingleton<IRefreshSessionDal>(new StubRefreshSessionDal());

                if (allowLoopback)
                {
                    // 把回环加入 KnownIPNetworks，让 ForwardedHeadersMiddleware 信任来自本机的转发头。
                    services.PostConfigure<ForwardedHeadersOptions>(options =>
                        options.KnownIPNetworks.Add(new System.Net.IPNetwork(
                            System.Net.IPAddress.Loopback, 32)));
                }
            });
        });

    private sealed class StubUserDal(UserAccount user, string? salt) : IUserDal
    {
        public Task<UserAccount?> ValidateCredentialsAsync(string userName, string passwordHash, CancellationToken cancellationToken)
            => Task.FromResult<UserAccount?>(user);
        public Task<string?> GetPasswordSaltAsync(string userName, CancellationToken cancellationToken)
            => Task.FromResult(salt);
        public Task<IReadOnlyList<UserListItem>> ListAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<UserListItem>>([new UserListItem(user.Id, user.UserName, user.Roles, false)]);
        public Task<RevocationSnapshot?> GetRevocationSnapshotAsync(Guid userId, CancellationToken cancellationToken)
            => Task.FromResult<RevocationSnapshot?>(user.Id == userId
                ? new RevocationSnapshot(user.SecurityStamp, false)
                : null);
        public Task AssignRolesAsync(Guid userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class StubCredentialDal : IUserCredentialDal
    {
        public Task<UserAccount> CreateAsync(string userName, IReadOnlyCollection<string> roles, CancellationToken cancellationToken)
            => Task.FromResult(new UserAccount(Guid.NewGuid(), userName, [.. roles]));
        public Task SetInitialPasswordAsync(Guid userId, string passwordHash, CancellationToken cancellationToken)
            => Task.CompletedTask;
        public Task<bool> ChangePasswordAsync(Guid userId, string currentPasswordHash, string newPasswordHash, CancellationToken cancellationToken)
            => Task.FromResult(true);
        public Task ResetPasswordAsync(Guid userId, string newPasswordHash, CancellationToken cancellationToken)
            => Task.CompletedTask;
        public Task<bool> DeleteAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(true);
        public Task SetLockoutAsync(
            Guid userId,
            DateTimeOffset? lockoutEnd,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubRefreshSessionDal : IRefreshSessionDal
    {
        public Task<RefreshToken> CreateAsync(Guid userId, CancellationToken cancellationToken)
            => Task.FromResult(new RefreshToken("stub-refresh-token", DateTimeOffset.UtcNow.AddDays(7)));
        public Task<RefreshSession?> RotateAsync(string value, CancellationToken cancellationToken)
            => Task.FromResult<RefreshSession?>(null);
        public Task RevokeAsync(string value, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RevokeAllAsync(Guid userId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<UserAccount?> GetUserByTokenAsync(string value, CancellationToken cancellationToken) =>
            Task.FromResult<UserAccount?>(null);
    }
}