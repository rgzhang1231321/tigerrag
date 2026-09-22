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
    // 启动期幂等补齐 Admin 角色的 [MenuEndpoint] 授权。存量数据库里 Admin 角色无 grant 时，
    // 首次启动自动灌全；后续启动 ListByRoleAsync 已含全部 endpoint，秒级返回不写库。
    using (var scope = app.Services.CreateScope())
    {
        var bootstrapper = scope.ServiceProvider.GetRequiredService<TigerRAG.Application.Auth.IAdminBootstrapper>();
        await bootstrapper.EnsureAdminGrantsAsync(CancellationToken.None);
    }
    await app.RunAsync();
}
catch (Exception ex)
{
    // 启动期任何失败（配置缺失 / builder 装配 / 端口占用 / bootstrap 异常）：先走结构化日志通道，再 console.stderr 打一行摘要，rethrow 让进程非零退出。
    using var loggerFactory = LoggerFactory.Create(builder => builder.AddSimpleConsole());
    loggerFactory.CreateLogger("TigerRAG.Api.Program")
        .LogCritical(ex, "TigerRAG startup failed: {Message}", ex.Message);
    Console.Error.WriteLine($"[FATAL] TigerRAG startup failed: {ex.GetType().Name}: {ex.Message}");
    throw;
}

public partial class Program;
