using Npgsql;

namespace TigerRAG.IntegrationTests.Infrastructure;

/// <summary>
/// 检测本地 Postgres 是否可用；不可用则按现有约定 fail loud（与 <c>DalBehaviorTests</c> 一致）。
/// 不引入 Testcontainers 等额外依赖；开发机本地有 Postgres 即可跑。
/// 凭据与 .env 中 <c>POSTGRES_DB</c> / <c>POSTGRES_USER</c> / <c>POSTGRES_PASSWORD</c> 一致。
/// </summary>
public static class LocalPostgresFixture
{
    public const string ConnectionString = "Host=localhost;Port=5432;Database=ragdb;Username=tigerrag;Password=And@2088;Timeout=2;Include Error Detail=true";

    public static async Task<bool> IsAvailableAsync()
    {
        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }
}