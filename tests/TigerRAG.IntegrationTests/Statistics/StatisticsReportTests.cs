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
using TigerRAG.Application.Auth;
using TigerRAG.Application.Shared;
using TigerRAG.Application.Users;
using TigerRAG.Infrastructure.Identity;
using TigerRAG.IntegrationTests.Api;

namespace TigerRAG.IntegrationTests.Statistics;

/// <summary>报表端点契约：各类报表返回正确数据结构，导出返回 CSV。</summary>
public sealed class StatisticsReportTests : IClassFixture<TigerRagApiFactory>
{
    private readonly TigerRagApiFactory _factory;
    private readonly HttpClient _client;

    public StatisticsReportTests(TigerRagApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task Reports_AnonymousRequest_ReturnsUnauthorized()
    {
        using var response = await _client.PostAsJsonAsync("/api/statistics/reports", new
        {
            reportType = 1,
            dateRange = new { start = "2026-09-01", end = "2026-09-20" }
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("code").GetInt32() > 0);
    }

    [Fact]
    public async Task Reports_Documents_ReturnsCorrectStructure()
    {
        await using var localFactory = new TigerRagApiFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IUserDal>();
                services.AddSingleton<IUserDal>(new MutableUserStore(
                    new UserAccount(Guid.NewGuid(), "report-admin", [SystemRoles.Admin]) { SecurityStamp = "stamp-2" }));
                services.RemoveAll<IUserCredentialDal>();
                services.AddSingleton<IUserCredentialDal>(new NoopCredentialDal());
                services.RemoveAll<IRefreshSessionDal>();
                services.AddSingleton<IRefreshSessionDal>(new NoopRefreshSessionDal());
            });
        });
        using var client = localFactory.CreateClient();

        var token = await LoginAsync(client, "report-admin");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.PostAsJsonAsync("/api/statistics/reports", new
        {
            reportType = 1,
            dateRange = new { start = "2026-09-01T00:00:00Z", end = "2026-09-20T23:59:59Z" }
        });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        File.WriteAllText("test_response.json", json);
        using var body = JsonDocument.Parse(json);
        var flag = body.RootElement.GetProperty("flag").GetBoolean();
        var code = body.RootElement.GetProperty("code").GetInt32();
        var message = body.RootElement.GetProperty("message").GetString();
        Assert.True(flag, $"reports flag=false, code={code}, message={message}");
        var data = body.RootElement.GetProperty("data");
        Assert.True(data.TryGetProperty("type", out _));
        var innerData = data.GetProperty("data");
        Assert.True(innerData.TryGetProperty("UploadTrend", out _));
        Assert.True(innerData.TryGetProperty("StatusBreakdown", out _));
        Assert.True(innerData.TryGetProperty("ByKb", out _));
        Assert.True(innerData.TryGetProperty("Failures", out _));
    }

    [Fact]
    public async Task Reports_Users_ReturnsCorrectStructure()
    {
        await using var localFactory = new TigerRagApiFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IUserDal>();
                services.AddSingleton<IUserDal>(new MutableUserStore(
                    new UserAccount(Guid.NewGuid(), "report-admin", [SystemRoles.Admin]) { SecurityStamp = "stamp-3" }));
                services.RemoveAll<IUserCredentialDal>();
                services.AddSingleton<IUserCredentialDal>(new NoopCredentialDal());
                services.RemoveAll<IRefreshSessionDal>();
                services.AddSingleton<IRefreshSessionDal>(new NoopRefreshSessionDal());
            });
        });
        using var client = localFactory.CreateClient();

        var token = await LoginAsync(client, "report-admin");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.PostAsJsonAsync("/api/statistics/reports", new
        {
            reportType = 2,
            dateRange = new { start = "2026-09-01T00:00:00Z", end = "2026-09-20T23:59:59Z" }
        });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(json);
        var flag = body.RootElement.GetProperty("flag").GetBoolean();
        var code = body.RootElement.GetProperty("code").GetInt32();
        var message = body.RootElement.GetProperty("message").GetString();
        Assert.True(flag, $"reports flag=false, code={code}, message={message}");
        var data = body.RootElement.GetProperty("data");
        Assert.True(data.TryGetProperty("type", out _));
        var innerData = data.GetProperty("data");
        Assert.True(innerData.TryGetProperty("NewUserTrend", out _));
        Assert.True(innerData.TryGetProperty("ActiveUserTrend", out _));
        Assert.True(innerData.TryGetProperty("RoleDistribution", out _));
    }

    [Fact]
    public async Task Reports_Export_ReturnsCsv()
    {
        await using var localFactory = new TigerRagApiFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IUserDal>();
                services.AddSingleton<IUserDal>(new MutableUserStore(
                    new UserAccount(Guid.NewGuid(), "report-admin", [SystemRoles.Admin]) { SecurityStamp = "stamp-4" }));
                services.RemoveAll<IUserCredentialDal>();
                services.AddSingleton<IUserCredentialDal>(new NoopCredentialDal());
                services.RemoveAll<IRefreshSessionDal>();
                services.AddSingleton<IRefreshSessionDal>(new NoopRefreshSessionDal());
            });
        });
        using var client = localFactory.CreateClient();

        var token = await LoginAsync(client, "report-admin");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.PostAsJsonAsync("/api/statistics/reports/export", new
        {
            reportType = 1,
            dateRange = new { start = "2026-09-01T00:00:00Z", end = "2026-09-20T23:59:59Z" }
        });
        response.EnsureSuccessStatusCode();
        Assert.Equal("text/csv; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        var csv = await response.Content.ReadAsStringAsync();
        Assert.Contains("报表类型", csv);
        Assert.Contains("日期范围", csv);
    }

    private static async Task<string> LoginAsync(HttpClient client, string userName)
    {
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            userName,
            passwordHash = new string('c', 32),
        });
        var loginJson = await loginResponse.Content.ReadAsStringAsync();
        using var loginBody = JsonDocument.Parse(loginJson);
        return loginBody.RootElement.GetProperty("data").GetProperty("accessToken").GetString()!;
    }

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
        public Task ResetPasswordAsync(Guid userId, string passwordHash, CancellationToken cancellationToken) => Task.CompletedTask;
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
        public Task<UserAccount?> GetUserByTokenAsync(string value, CancellationToken cancellationToken) =>
            Task.FromResult<UserAccount?>(null);
    }
}
