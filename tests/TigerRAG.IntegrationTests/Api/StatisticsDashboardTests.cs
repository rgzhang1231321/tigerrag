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
using TigerRAG.Infrastructure.Identity;

namespace TigerRAG.IntegrationTests.Api;

/// <summary>Dashboard 统计端点契约：未授权拒绝、合法 token 返回聚合指标。</summary>
public sealed class StatisticsDashboardTests : IClassFixture<TigerRagApiFactory>
{
    private readonly TigerRagApiFactory _factory;
    private readonly HttpClient _client;

    public StatisticsDashboardTests(TigerRagApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task Dashboard_AnonymousRequest_ReturnsUnauthorized()
    {
        using var response = await _client.PostAsJsonAsync("/api/statistics/dashboard", new { });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("code").GetInt32() > 0);
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("data").ValueKind);
    }

    [Fact]
    public async Task Dashboard_AdminRequest_ReturnsAggregatedMetrics()
    {
        var userId = Guid.NewGuid();
        using var localFactory = new TigerRagApiFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IUserDal>();
                services.AddSingleton<IUserDal>(new MutableUserStore(
                    new UserAccount(userId, "dashboard-admin", [SystemRoles.Admin]) { SecurityStamp = "stamp-1" }));
                services.RemoveAll<IUserCredentialDal>();
                services.AddSingleton<IUserCredentialDal>(new NoopCredentialDal());
                services.RemoveAll<IRefreshSessionDal>();
                services.AddSingleton<IRefreshSessionDal>(new NoopRefreshSessionDal());
            });
        });
        using var client = localFactory.CreateClient();

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            userName = "dashboard-admin",
            passwordHash = new string('c', 32),
        });
        var loginJson = await loginResponse.Content.ReadAsStringAsync();
        loginResponse.EnsureSuccessStatusCode();
        using var loginBody = JsonDocument.Parse(loginJson);
        var loginFlag = loginBody.RootElement.GetProperty("flag").GetBoolean();
        var loginCode = loginBody.RootElement.GetProperty("code").GetInt32();
        var loginMessage = loginBody.RootElement.GetProperty("message").GetString();
        Assert.True(loginFlag, $"login failed: code={loginCode}, message={loginMessage}");
        var token = loginBody.RootElement.GetProperty("data").GetProperty("accessToken").GetString()!;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.PostAsJsonAsync("/api/statistics/dashboard", new { });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(json);
        var flag = body.RootElement.GetProperty("flag").GetBoolean();
        var code = body.RootElement.GetProperty("code").GetInt32();
        var message = body.RootElement.GetProperty("message").GetString();
        Assert.True(flag, $"dashboard flag=false, code={code}, message={message}");
        var data = body.RootElement.GetProperty("data");
        Assert.True(data.TryGetProperty("knowledgeBaseCount", out _));
        Assert.True(data.TryGetProperty("documentCount", out _));
        Assert.True(data.TryGetProperty("indexedDocumentCount", out _));
        Assert.True(data.TryGetProperty("processingDocumentCount", out _));
        Assert.True(data.TryGetProperty("failedDocumentCount", out _));
        Assert.True(data.TryGetProperty("userCount", out _));
        Assert.True(data.TryGetProperty("conversationCount", out _));
        Assert.True(data.TryGetProperty("messageCount", out _));
        Assert.True(data.TryGetProperty("totalTokens", out _));
        Assert.True(data.TryGetProperty("recentWeekDocuments", out _));
        Assert.True(data.TryGetProperty("documentsByKb", out _));
        Assert.True(data.TryGetProperty("messagesPerDay", out _));
    }

    /// <summary>承载 stamp 与凭证的可写 DAL 替身；让 /api/auth/login 与 JWT 校验都通过。</summary>
    private sealed class MutableUserStore(UserAccount initial) : IUserDal
    {
        private UserAccount? _current = initial;

        public Task<UserAccount?> ValidateCredentialsAsync(string userName, string passwordHash, CancellationToken cancellationToken) =>
            Task.FromResult(_current?.UserName == userName ? _current : null);

        public Task<string?> GetPasswordSaltAsync(string userName, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(_current?.UserName == userName ? "test-salt" : null);

        public Task<IReadOnlyList<UserListItem>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserListItem>>(_current is null
                ? []
                : [new UserListItem(_current.Id, _current.UserName, _current.Roles, false)]);

        public Task<RevocationSnapshot?> GetRevocationSnapshotAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<RevocationSnapshot?>(_current is null || _current.Id != userId
                ? null
                : new RevocationSnapshot(_current.SecurityStamp, false));

        public Task AssignRolesAsync(Guid userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class NoopCredentialDal : IUserCredentialDal
    {
        public Task<UserAccount> CreateAsync(string userName, IReadOnlyCollection<string> roles, CancellationToken cancellationToken) =>
            Task.FromResult(new UserAccount(Guid.NewGuid(), userName, [.. roles]) { SecurityStamp = "noop" });
        public Task SetInitialPasswordAsync(Guid userId, string passwordHash, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> ChangePasswordAsync(Guid userId, string currentPasswordHash, string newPasswordHash, CancellationToken cancellationToken) => Task.FromResult(true);
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
