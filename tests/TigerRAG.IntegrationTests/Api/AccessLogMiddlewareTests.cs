using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TigerRAG.Api.Common;
using TigerRAG.Api.Middleware;
using TigerRAG.Infrastructure.Logging;

namespace TigerRAG.IntegrationTests.Api;

/// <summary>
/// 手工搭建 RequestId → AccessLog → ApiResponse → terminal 管道，
/// 锁定 AccessLogMiddleware 的核心行为：字段采集完整、请求体脱敏且回卷、
/// 真实状态码来自暂存契约、非 /api 路径与关闭开关不记录、防御性 catch 不吞异常。
/// </summary>
[Collection(nameof(AccessLogBufferCollection))]
public sealed class AccessLogMiddlewareTests
{
    public AccessLogMiddlewareTests()
    {
        // 静态缓冲跨测试共享：先清残留再配置，保证每个用例从干净状态开始。
        AccessLogBuffer.ResetForTest();
        AccessLogBuffer.Configure(new AccessLogConfiguration(), new ConfigurationBuilder().Build());
    }

    [Fact]
    public async Task JsonPost_CapturesAllFields_MasksBody_AndRewindsBodyForTerminal()
    {
        var terminalBody = string.Empty;
        var (httpContext, requestId) = await InvokePipelineAsync(
            async ctx =>
            {
                // terminal 模拟 MVC 绑定：必须能读到完整原始请求体（回卷证明）。
                terminalBody = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
                ctx.Response.StatusCode = StatusCodes.Status200OK;
                await ctx.Response.WriteAsync("""{"ok":true}""");
            },
            request =>
            {
                request.Method = "POST";
                request.Path = "/api/auth/login";
                request.QueryString = new QueryString("?trace=true");
                var bytes = Encoding.UTF8.GetBytes(
                    """{"userName":"admin","passwordHash":"secret-value-123"}""");
                request.Body = new MemoryStream(bytes);
                request.ContentLength = bytes.Length;
                request.ContentType = "application/json";
            });

        var entry = AccessLogBuffer.DrainForTest().Single(e => e.RequestId == requestId);
        Assert.Equal("POST", entry.HttpMethod);
        Assert.Equal("/api/auth/login", entry.RequestPath);
        Assert.Equal("?trace=true", entry.QueryString);
        // 无路由端点（DefaultHttpContext 未接路由）→ action 为 null。
        Assert.Null(entry.Action);
        // 匿名请求（无 claims）→ 用户字段为 null。
        Assert.Null(entry.UserId);
        Assert.Null(entry.UserName);
        // 请求体脱敏：不含原文密文，其余字段保留。
        Assert.Contains("\"***\"", entry.RequestBody);
        Assert.DoesNotContain("secret-value-123", entry.RequestBody);
        Assert.Contains("admin", entry.RequestBody);
        // 成功请求：真实状态 200、不记录响应体。
        Assert.Equal(StatusCodes.Status200OK, entry.StatusCode);
        Assert.Null(entry.ResponseBody);
        Assert.True(entry.ElapsedMs >= 0);
        // terminal 读到了完整原始体：中间件的读体没有破坏 MVC 绑定。
        Assert.Contains("secret-value-123", terminalBody);
        Assert.True(httpContext.Response.Body.Length > 0);
    }

    [Fact]
    public async Task JsonPost_NestedSensitiveKey_Masked()
    {
        var (_, requestId) = await InvokePipelineAsync(
            ctx => Task.CompletedTask,
            request =>
            {
                request.Method = "POST";
                request.Path = "/api/users/change-password";
                var bytes = Encoding.UTF8.GetBytes(
                    """{"userName":"admin","profile":{"currentPasswordHash":"abc123","displayName":"张三"}}""");
                request.Body = new MemoryStream(bytes);
                request.ContentLength = bytes.Length;
                request.ContentType = "application/json";
            });

        var entry = AccessLogBuffer.DrainForTest().Single(e => e.RequestId == requestId);
        // 嵌套敏感 key 同样脱敏，非敏感兄弟字段保留。
        Assert.DoesNotContain("abc123", entry.RequestBody);
        Assert.Contains("\"***\"", entry.RequestBody);
        Assert.Contains("张三", entry.RequestBody);
    }

    [Fact]
    public async Task NextThrows_Enqueues500_AndRethrows()
    {
        // 不经过 ApiResponseMiddleware：直接对抛异常的 next 测防御性 catch。
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "POST";
        httpContext.Request.Path = "/api/users/list";
        var accessLog = new AccessLogMiddleware(
            _ => throw new InvalidOperationException("synthetic middleware failure"),
            new AccessLogConfiguration(),
            NullLogger<AccessLogMiddleware>.Instance);
        var requestIdMiddleware = new RequestIdMiddleware(accessLog.InvokeAsync);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => requestIdMiddleware.InvokeAsync(httpContext));

        Assert.Equal("synthetic middleware failure", thrown.Message);
        // RequestIdMiddleware 在调 next 之前已写入 Items，抛出后仍可读取。
        var requestId = (string)httpContext.Items[RequestIdKeys.ItemKey]!;
        var entry = AccessLogBuffer.DrainForTest().Single(e => e.RequestId == requestId);
        // 防御路径：500 + 重抛（保持 UseExceptionHandler 兜底语义）。
        Assert.Equal(StatusCodes.Status500InternalServerError, entry.StatusCode);
        Assert.Contains("[50000]", entry.ResponseBody);
        Assert.Contains("synthetic middleware failure", entry.ResponseBody);
    }

    [Fact]
    public async Task RequestAbortedWithOce_Enqueues499_AndDoesNotThrow()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "POST";
        httpContext.Request.Path = "/api/documents/upload";
        // 直接种入已取消的令牌：比 Abort() 更确定地触发 OCE-when-RequestAborted 分支。
        httpContext.RequestAborted = new CancellationToken(canceled: true);

        var accessLog = new AccessLogMiddleware(
            _ => throw new OperationCanceledException(httpContext.RequestAborted),
            new AccessLogConfiguration(),
            NullLogger<AccessLogMiddleware>.Instance);
        var requestIdMiddleware = new RequestIdMiddleware(accessLog.InvokeAsync);

        // 客户端已断开：OCE 不应重抛（响应无人接收），按 nginx 惯例记 499。
        await requestIdMiddleware.InvokeAsync(httpContext);

        var requestId = (string)httpContext.Items[RequestIdKeys.ItemKey]!;
        var entry = AccessLogBuffer.DrainForTest().Single(e => e.RequestId == requestId);
        Assert.Equal(499, entry.StatusCode);
        Assert.Contains("[499]", entry.ResponseBody);
    }

    [Fact]
    public async Task NonApiPath_HubsChat_DoesNotEnqueue()
    {
        var (_, requestId) = await InvokePipelineAsync(
            _ => Task.CompletedTask,
            request =>
            {
                request.Method = "GET";
                request.Path = "/hubs/chat";
            });

        // /hubs/chat 不以 /api 开头：不记录。
        Assert.DoesNotContain(AccessLogBuffer.DrainForTest(), e => e.RequestId == requestId);
    }

    [Fact]
    public async Task MultipartRequest_RecordsSummaryWithoutReadingBody()
    {
        var (httpContext, requestId) = await InvokePipelineAsync(
            _ => Task.CompletedTask,
            request =>
            {
                request.Method = "POST";
                request.Path = "/api/documents/upload";
                var bytes = Encoding.UTF8.GetBytes("--boundary\r\ncontent\r\n--boundary--");
                request.Body = new MemoryStream(bytes);
                request.ContentLength = bytes.Length;
                request.ContentType = "multipart/form-data; boundary=boundary";
            });

        var entry = AccessLogBuffer.DrainForTest().Single(e => e.RequestId == requestId);
        // multipart 只记摘要，不读体：位置必须保持 0（未消费），供后续 Form 解析。
        Assert.Contains("multipart/form-data", entry.RequestBody);
        Assert.Contains("content-length=", entry.RequestBody);
        Assert.Equal(0, httpContext.Request.Body.Position);
    }

    [Fact]
    public async Task DisabledConfiguration_DoesNotEnqueue()
    {
        var (_, requestId) = await InvokePipelineAsync(
            _ => Task.CompletedTask,
            request =>
            {
                request.Method = "POST";
                request.Path = "/api/users/list";
            },
            configuration: new AccessLogConfiguration { Enabled = false });

        // 开关关闭：直通不记录。
        Assert.DoesNotContain(AccessLogBuffer.DrainForTest(), e => e.RequestId == requestId);
    }

    [Fact]
    public async Task TerminalSets401_EnqueuesRealStatusWithFailureBody()
    {
        var (_, requestId) = await InvokePipelineAsync(
            ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            },
            request =>
            {
                request.Method = "POST";
                request.Path = "/api/knowledge-bases/list";
            });

        var entry = AccessLogBuffer.DrainForTest().Single(e => e.RequestId == requestId);
        // ApiResponseMiddleware 已把响应改写为 200，真实 401 与失败信息只能来自暂存契约。
        Assert.Equal(StatusCodes.Status401Unauthorized, entry.StatusCode);
        Assert.Contains("[40100]", entry.ResponseBody);
        // 对外响应仍是 200 信封。
    }

    [Fact]
    public async Task AuthenticatedClaims_CapturedAsUserIdAndName()
    {
        var userId = Guid.NewGuid();
        var (_, requestId) = await InvokePipelineAsync(
            _ => Task.CompletedTask,
            request =>
            {
                request.Method = "POST";
                request.Path = "/api/users/list";
            },
            context =>
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                    new Claim(ClaimTypes.Name, "alice")
                ]));
            });

        var entry = AccessLogBuffer.DrainForTest().Single(e => e.RequestId == requestId);
        Assert.Equal(userId, entry.UserId);
        Assert.Equal("alice", entry.UserName);
    }

    [Fact]
    public async Task RemoteIpAddress_Captured()
    {
        var (_, requestId) = await InvokePipelineAsync(
            _ => Task.CompletedTask,
            request =>
            {
                request.Method = "POST";
                request.Path = "/api/users/list";
            },
            context => context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7"));

        var entry = AccessLogBuffer.DrainForTest().Single(e => e.RequestId == requestId);
        Assert.Equal("203.0.113.7", entry.Ip);
    }

    /// <summary>搭建 RequestId → AccessLog → ApiResponse → terminal 完整管道并执行。</summary>
    private static async Task<(HttpContext Context, string RequestId)> InvokePipelineAsync(
        RequestDelegate terminal,
        Action<HttpRequest>? configureRequest = null,
        Action<HttpContext>? configureContext = null,
        AccessLogConfiguration? configuration = null)
    {
        var httpContext = new DefaultHttpContext();
        // DefaultHttpContext 的默认响应体是 Stream.Null（写入即丢），换成本地 MemoryStream 才能断言内容。
        httpContext.Response.Body = new MemoryStream();
        configureContext?.Invoke(httpContext);
        configureRequest?.Invoke(httpContext.Request);

        var apiResponse = new ApiResponseMiddleware(terminal, NullLogger<ApiResponseMiddleware>.Instance);
        var accessLog = new AccessLogMiddleware(
            apiResponse.InvokeAsync,
            configuration ?? new AccessLogConfiguration(),
            NullLogger<AccessLogMiddleware>.Instance);
        var requestIdMiddleware = new RequestIdMiddleware(accessLog.InvokeAsync);

        await requestIdMiddleware.InvokeAsync(httpContext);
        return (httpContext, (string)httpContext.Items[RequestIdKeys.ItemKey]!);
    }
}
