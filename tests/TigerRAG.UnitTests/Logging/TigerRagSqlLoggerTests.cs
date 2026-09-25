using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TigerRAG.Infrastructure.Logging;

namespace TigerRAG.UnitTests.Logging;

/// <summary>
/// 验证 <see cref="TigerRagSqlLogger"/> 在每次 Log 调用时把当前 HTTP 请求的 RequestId 写入条目；
/// requestId 是用户排查问题时前后端对齐的唯一抓手，不能丢。
/// </summary>
[Collection(nameof(ApiLogBufferCollection))]
public sealed class TigerRagSqlLoggerTests
{
    public TigerRagSqlLoggerTests()
    {
        // 各测试间共享静态缓冲，必须先 reset 再手动 configure 才能入队。
        ApiLogBuffer.ResetForTest();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        ApiLogBuffer.Configure(new ApiLogConfiguration(), config);
    }
    [Fact]
    public void Log_WithRequestInHttpContext_EntryCarriesRequestId()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items[RequestIdKeys.ItemKey] = "req-abc";
        var logger = new TigerRagSqlLogger("TigerRAG.UnitTests", accessor, new ApiLogConfiguration());

        logger.Log(
            LogLevel.Error,
            eventId: default,
            "boom: Who",
            new InvalidOperationException("inner"),
            (state, _) => state?.ToString() ?? string.Empty);

        var entries = ApiLogBuffer.DrainForTest();
        var entry = Assert.Single(entries);
        Assert.Equal("req-abc", entry.RequestId);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal("TigerRAG.UnitTests", entry.SourceContext);
        Assert.Contains("boom: Who", entry.Message);
        Assert.Contains("inner", entry.Exception);
        // 消息行判别契约：kind 恒为 message，访问维度字段恒为空，保证两类行在单表里互不混淆。
        Assert.Equal("message", entry.Kind);
        Assert.Null(entry.UserName);
        Assert.Null(entry.Action);
        Assert.Null(entry.StatusCode);
        Assert.Null(entry.RequestBody);
        Assert.Null(entry.ResponseBody);
    }

    [Fact]
    public void Log_WithoutHttpContext_EntryHasEmptyRequestIdInsteadOfThrowing()
    {
        // 后台任务 / 启动期日志没有 HttpContext，logger 必须降级而不是炸。
        var logger = new TigerRagSqlLogger("Bg", new HttpContextAccessor(), new ApiLogConfiguration());

        logger.Log(
            LogLevel.Error,
            eventId: default,
            "hello",
            exception: null,
            (state, _) => state?.ToString() ?? string.Empty);

        var entry = Assert.Single(ApiLogBuffer.DrainForTest());
        Assert.Equal(string.Empty, entry.RequestId);
    }
}