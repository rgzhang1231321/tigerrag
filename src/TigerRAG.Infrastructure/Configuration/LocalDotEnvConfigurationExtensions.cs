using Microsoft.Extensions.Configuration;

namespace TigerRAG.Infrastructure.Configuration;

/// <summary>
/// 仅在 Development / Test 环境加载本地 .env，并通过内存层覆盖环境变量。
/// 不读取生产机密，生产密钥由 Secret Secret / 环境变量注入。
/// </summary>
public static class LocalDotEnvConfigurationExtensions
{
    public static IConfigurationManager AddLocalDotEnv(
        this IConfigurationManager configuration,
        string environmentName,
        string basePath,
        IDictionary<string, string?>? environmentVariables = null)
    {
        if (environmentName.Equals("Development", StringComparison.OrdinalIgnoreCase)
            || environmentName.Equals("Test", StringComparison.OrdinalIgnoreCase))
        {
            // 向父目录逐级查找 .env，便于在子项目目录下启动也能命中根 .env。
            var dotenvPath = FindDotEnv(basePath);
            if (dotenvPath is not null)
            {
                configuration.AddInMemoryCollection(ParseDotEnv(dotenvPath));
            }
        }

        if (environmentVariables is not null)
        {
            configuration.AddInMemoryCollection(environmentVariables.ToDictionary(
                pair => ToConfigurationKey(pair.Key),
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase));
        }
        else
        {
            configuration.AddEnvironmentVariables();
        }

        return configuration;
    }

    private static Dictionary<string, string?> ParseDotEnv(string path)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (value.Length >= 2 && value[0] == value[^1] && (value[0] == '\'' || value[0] == '"'))
            {
                value = value[1..^1];
            }

            values[ToConfigurationKey(key)] = value;
        }

        // .env 与 docker-compose 风格保持一致；自动拼接连接串减少手工配置。
        if (values.TryGetValue("POSTGRES_DB", out var database)
            && values.TryGetValue("POSTGRES_USER", out var username)
            && values.TryGetValue("POSTGRES_PASSWORD", out var password))
        {
            values["ConnectionStrings:PostgreSql"] =
                $"Host=localhost;Database={database};Username={username};Password={password}";
        }

        if (values.TryGetValue("REDIS_PASSWORD", out var redisPassword))
        {
            values["ConnectionStrings:Redis"] = $"localhost:6379,password={redisPassword},abortConnect=false";
        }

        return values;
    }

    private static string? FindDotEnv(string basePath)
    {
        var directory = new DirectoryInfo(basePath);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, ".env");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static string ToConfigurationKey(string key) => key switch
    {
        "JWT_SIGNING_KEY" => "Jwt:SigningKey",
        "JWT_ISSUER" => "Jwt:Issuer",
        "JWT_AUDIENCE" => "Jwt:Audience",
        "POSTGRES_DB" => "POSTGRES_DB",
        "POSTGRES_USER" => "POSTGRES_USER",
        "POSTGRES_PASSWORD" => "POSTGRES_PASSWORD",
        "REDIS_PASSWORD" => "REDIS_PASSWORD",
        "MINIO_ROOT_USER" => "Services:Minio:AccessKey",
        "MINIO_ROOT_PASSWORD" => "Services:Minio:SecretKey",
        _ when key.Contains("__", StringComparison.Ordinal) => key.Replace("__", ":", StringComparison.Ordinal),
        _ => key
    };
}
