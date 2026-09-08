using Microsoft.Extensions.Configuration;
using TigerRAG.Infrastructure.Configuration;

namespace TigerRAG.UnitTests.Configuration;

public sealed class LocalDotEnvConfigurationTests
{
    [Fact]
    public void AddLocalDotEnv_LoadsDotEnvOnlyForDevelopmentAndEnvironmentWins()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, ".env"),
                "JWT_SIGNING_KEY=from-file\nCUSTOM__VALUE=from-file\n");

            var configuration = new ConfigurationManager();
            configuration.AddLocalDotEnv("Development", root.FullName,
                new Dictionary<string, string?> { ["JWT_SIGNING_KEY"] = "from-environment" });

            Assert.Equal("from-environment", configuration["Jwt:SigningKey"]);
            Assert.Equal("from-file", configuration["CUSTOM:VALUE"]);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [Fact]
    public void AddLocalDotEnv_DoesNotReadDotEnvInProduction()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, ".env"), "CUSTOM__VALUE=from-file\n");

            var configuration = new ConfigurationManager();
            configuration.AddLocalDotEnv("Production", root.FullName);

            Assert.Null(configuration["CUSTOM:VALUE"]);
        }
        finally
        {
            root.Delete(true);
        }
    }
}
