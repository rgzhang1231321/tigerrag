using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TigerRAG.Api;
using TigerRAG.Application.Security;

namespace TigerRAG.IntegrationTests.Api;

public sealed class AdminBootstrapCommandTests
{
    [Fact]
    public async Task TryRunAsync_WithoutFlag_DoesNotProvisionAdmin()
    {
        var bootstrapper = new RecordingAdminBootstrapper();

        var handled = await AdminBootstrapCommand.TryRunAsync(
            Services(bootstrapper),
            new ConfigurationBuilder().Build(),
            [],
            CancellationToken.None);

        Assert.False(handled);
        Assert.Null(bootstrapper.UserName);
    }

    [Fact]
    public async Task TryRunAsync_WithFlag_ProvisionsConfiguredAdmin()
    {
        var bootstrapper = new RecordingAdminBootstrapper();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BOOTSTRAP_ADMIN_USERNAME"] = "admin",
                ["BOOTSTRAP_ADMIN_PASSWORD"] = "initial-password"
            })
            .Build();

        var handled = await AdminBootstrapCommand.TryRunAsync(
            Services(bootstrapper),
            configuration,
            ["--bootstrap-admin"],
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal("admin", bootstrapper.UserName);
        Assert.Equal("initial-password", bootstrapper.Password);
    }

    private static IServiceProvider Services(IAdminBootstrapper bootstrapper)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAdminBootstrapper>(bootstrapper);
        return services.BuildServiceProvider();
    }

    private sealed class RecordingAdminBootstrapper : IAdminBootstrapper
    {
        public string? UserName { get; private set; }
        public string? Password { get; private set; }

        public Task BootstrapAsync(string userName, string password, CancellationToken cancellationToken)
        {
            UserName = userName;
            Password = password;
            return Task.CompletedTask;
        }
    }
}
