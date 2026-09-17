using TigerRAG.Application.Security;

namespace TigerRAG.Api;

/// <summary>
/// 首次部署管理员引导命令。仅在 <c>--bootstrap-admin</c> 启动参数下生效；常规启动不会创建账号。
/// 凭据从 <c>BOOTSTRAP_ADMIN_USERNAME</c> / <c>BOOTSTRAP_ADMIN_PASSWORD</c> 环境变量读取，避免命令行泄露。
/// </summary>
public static class AdminBootstrapCommand
{
    public static async Task<bool> TryRunAsync(
        IServiceProvider services,
        IConfiguration configuration,
        IReadOnlyCollection<string> args,
        CancellationToken cancellationToken)
    {
        if (!args.Contains("--bootstrap-admin", StringComparer.Ordinal))
        {
            return false;
        }

        var userName = configuration["BOOTSTRAP_ADMIN_USERNAME"];
        var password = configuration["BOOTSTRAP_ADMIN_PASSWORD"];
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "BOOTSTRAP_ADMIN_USERNAME and BOOTSTRAP_ADMIN_PASSWORD are required.");
        }

        using var scope = services.CreateScope();
        var bootstrapper = scope.ServiceProvider.GetRequiredService<IAdminBootstrapper>();
        await bootstrapper.BootstrapAsync(userName, password, cancellationToken);
        return true;
    }
}
