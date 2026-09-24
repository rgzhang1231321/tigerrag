using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TigerRAG.Api;
using TigerRAG.Infrastructure;
using TigerRAG.Infrastructure.Logging;

try
{
    var builder = WebApplication.CreateBuilder(args);
    // 清空默认日志提供器，全部由自定义 TigerRagSqlLoggerProvider 接管，条目落到 api_log 表。
    builder.Logging.ClearProviders().AddTigerRagSqlLogger(builder.Configuration);
    builder.Services.AddTigerRagInfrastructure(builder.Configuration);
    // 常规启动与一次性 bootstrap 共用 API 组合根（含认证栈）。
    // bootstrap 分支不会调用 Run()，因此未映射的端点保持未激活状态。
    builder.Services.AddTigerRagApi(builder.Configuration);

    if (args.Contains("--bootstrap-admin", StringComparer.Ordinal))
    {
        var bootstrapApp = builder.Build();
        await AdminBootstrapCommand.TryRunAsync(
            bootstrapApp.Services,
            bootstrapApp.Configuration,
            args,
            CancellationToken.None);
        return;
    }

    var app = builder.Build();
    app.MapTigerRagApi();
    await app.RunAsync();
}
catch (Exception ex)
{
    // 启动期异常写本地文件，便于数据库日志不可用时排查。
    var logDir = Path.Combine(AppContext.BaseDirectory, "logs");
    Directory.CreateDirectory(logDir);
    var logFile = Path.Combine(logDir, $"startup-fail-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
    var content = $"""
        [{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] TigerRAG API 启动失败

        异常类型: {ex.GetType().FullName}
        异常消息: {ex.Message}

        堆栈跟踪:
        {ex.StackTrace}

        {(ex.InnerException is not null ? $"内部异常: {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}\n\n{ex.InnerException.StackTrace}" : "")}
        """;
    File.WriteAllText(logFile, content);
    Console.Error.WriteLine($"[FATAL] 异常详情已写入: {logFile}");
}

public partial class Program;
