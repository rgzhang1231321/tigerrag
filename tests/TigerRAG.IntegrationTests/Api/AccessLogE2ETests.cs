using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using TigerRAG.Application.Users;
using TigerRAG.Infrastructure.Logging;

namespace TigerRAG.IntegrationTests.Api;

/// <summary>
/// 端到端锁定访问日志契约：真实管道（路由 → 认证 → filter → 中间件改写）下，
/// 每条 /api 请求在 ApiLogBuffer 留下一条 kind='access' 字段正确的条目；
/// 业务失败与异常路径的真实状态码、失败信息来自暂存契约；
/// 异常在同 requestId 的 api_log 缓冲里留有 kind='message' 的 Error 行 — 单表自关联契约。
/// </summary>
[Collection(nameof(AccessLogBufferCollection))]
public sealed class AccessLogE2ETests
{
    public AccessLogE2ETests()
    {
        // 静态缓冲跨测试共享：工厂创建前清残留（工厂创建时会重新 Configure）。
        ApiLogBuffer.ResetForTest();
    }

    [Fact]
    public async Task SaltEndpoint_Success_EnqueuesCompleteAccessEntry()
    {
        using var factory = CreateFactory(new StubUserDal("c2FsdA=="));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/salt", new { userName = "admin" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var (requestId, code) = await ReadEnvelopeAsync(response);
        Assert.Equal(0, code);

        // 单表单缓冲：按 kind='access' 过滤出访问行（同 requestId 可能还有消息行）。
        var entry = ApiLogBuffer.DrainForTest().Single(e => e.RequestId == requestId && e.Kind == "access");
        Assert.Equal("POST /api/auth/salt", entry.RequestPath);
        Assert.StartsWith("POST /api/auth/salt 200 ", entry.Message);
        Assert.EndsWith("ms", entry.Message);
        // 真实路由命中 AuthController.GetSalt。
        Assert.Equal("Auth.GetSalt", entry.Action);
        // 匿名请求（未登录）：用户字段为 null。
        Assert.Null(entry.UserName);
        Assert.Contains("admin", entry.RequestBody);
        // 成功：真实状态 200，不记录响应体。
        Assert.Equal(200, entry.StatusCode);
        Assert.Null(entry.ResponseBody);
        Assert.True(entry.ElapsedMs >= 0);
    }

    [Fact]
    public async Task SaltEndpoint_UserMissing_EnqueuesBusinessFailureWithRealStatus200()
    {
        using var factory = CreateFactory(new StubUserDal(null));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/salt", new { userName = "ghost" });

        // 业务失败对外是 HTTP 200 + code != 0。
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var (requestId, code) = await ReadEnvelopeAsync(response);
        Assert.Equal(40400, code);

        var entry = ApiLogBuffer.DrainForTest().Single(e => e.RequestId == requestId && e.Kind == "access");
        // 真实状态码确实是 200（业务失败），但响应体必须记录失败信息。
        Assert.Equal(200, entry.StatusCode);
        Assert.Equal("[40400] 用户不存在", entry.ResponseBody);
        Assert.Equal("Auth.GetSalt", entry.Action);
    }

    [Fact]
    public async Task UnknownApiPath_Enqueues404WithNullAction()
    {
        using var factory = CreateFactory(new StubUserDal(null));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/no-such-endpoint", new { });

        var (requestId, code) = await ReadEnvelopeAsync(response);
        Assert.Equal(40400, code);

        var entry = ApiLogBuffer.DrainForTest().Single(e => e.RequestId == requestId && e.Kind == "access");
        // 未匹配路由：真实 404（对外被改写为 200）、action 为 null。
        Assert.Equal(404, entry.StatusCode);
        Assert.Null(entry.Action);
        Assert.Contains("[40400]", entry.ResponseBody);
    }

    [Fact]
    public async Task SaltEndpoint_DalThrows_EnqueuesAccess500AndMessageErrorRowWithSameRequestId()
    {
        using var factory = CreateFactory(new ThrowingUserDal());
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/salt", new { userName = "admin" });

        var (requestId, code) = await ReadEnvelopeAsync(response);
        Assert.Equal(50000, code);

        // 单表自关联契约：同一次 Drain 里既有 access 行（500 + 失败信息），
        // 也有 kind='message' 的 Error 行（同 requestId 带堆栈），两类行互不混淆。
        var entries = ApiLogBuffer.DrainForTest().Where(e => e.RequestId == requestId).ToArray();
        var accessRow = Assert.Single(entries, e => e.Kind == "access");
        Assert.Equal(500, accessRow.StatusCode);
        Assert.Contains("[50000]", accessRow.ResponseBody);
        Assert.Contains("synthetic dal failure", accessRow.ResponseBody);

        var errorRow = Assert.Single(entries, e => e.Kind == "message" && e.Level == LogLevel.Error);
        Assert.Equal("message", errorRow.Kind);
        Assert.NotNull(errorRow.Exception);
        Assert.Contains("synthetic dal failure", errorRow.Exception);
    }

    /// <summary>创建替换 IUserDal 的测试工厂（不依赖真实数据库）。</summary>
    private static WebApplicationFactory<Program> CreateFactory(IUserDal userDal) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:Issuer", "TigerRAG.Tests");
            builder.UseSetting("Jwt:Audience", "TigerRAG.Tests");
            builder.UseSetting("Jwt:SigningKey", "test-only-signing-key-with-at-least-32-characters");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IUserDal>();
                services.AddSingleton(userDal);
            });
        });

    /// <summary>读取响应信封，返回 (requestId, code)。</summary>
    private static async Task<(string RequestId, int Code)> ReadEnvelopeAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        return (root.GetProperty("requestId").GetString()!, root.GetProperty("code").GetInt32());
    }

    /// <summary>按配置返回 salt 的桩 DAL；salt 为 null 模拟用户不存在。</summary>
    private sealed class StubUserDal(string? salt) : IUserDal
    {
        public Task<UserAccount?> ValidateCredentialsAsync(string userName, string passwordHash, CancellationToken cancellationToken) =>
            Task.FromResult<UserAccount?>(null);

        public Task<string?> GetPasswordSaltAsync(string userName, CancellationToken cancellationToken) =>
            Task.FromResult(salt);

        public Task<IReadOnlyList<UserListItem>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserListItem>>([]);

        public Task<RevocationSnapshot?> GetRevocationSnapshotAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<RevocationSnapshot?>(null);

        public Task AssignRolesAsync(Guid userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    /// <summary>所有方法都抛异常的 DAL，模拟基础设施故障。</summary>
    private sealed class ThrowingUserDal : IUserDal
    {
        public Task<UserAccount?> ValidateCredentialsAsync(string userName, string passwordHash, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("synthetic dal failure");

        public Task<string?> GetPasswordSaltAsync(string userName, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("synthetic dal failure");

        public Task<IReadOnlyList<UserListItem>> ListAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("synthetic dal failure");

        public Task<RevocationSnapshot?> GetRevocationSnapshotAsync(Guid userId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("synthetic dal failure");

        public Task AssignRolesAsync(Guid userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("synthetic dal failure");
    }
}
