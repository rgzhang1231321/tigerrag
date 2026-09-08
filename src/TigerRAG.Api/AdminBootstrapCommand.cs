using TigerRAG.Application.Security;

namespace TigerRAG.Api;

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

        var userName = configuration["BootstrapAdmin:UserName"];
        var password = configuration["BootstrapAdmin:Password"];
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "BootstrapAdmin:UserName and BootstrapAdmin:Password are required.");
        }

        using var scope = services.CreateScope();
        var bootstrapper = scope.ServiceProvider.GetRequiredService<IAdminBootstrapper>();
        await bootstrapper.BootstrapAsync(userName, password, cancellationToken);
        return true;
    }
}
