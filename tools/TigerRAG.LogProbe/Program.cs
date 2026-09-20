using System;
using System.IO;
using System.Threading.Tasks;
using Npgsql;

namespace TigerRAG.LogProbe;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        const string connectionString = "Host=localhost;Database=ragdb;Username=tigerrag;Password=And@2088";

        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: logprobe inspect | exec-sql <file>");
            return 1;
        }

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        if (args[0] == "inspect")
        {
            return await InspectAsync(conn);
        }

        if (args[0] == "exec-sql" && args.Length >= 2)
        {
            return await ExecSqlAsync(conn, args[1]);
        }

        Console.Error.WriteLine("Unknown command.");
        return 1;
    }

    private static async Task<int> InspectAsync(NpgsqlConnection conn)
    {
        var sql = @"SELECT column_name, data_type FROM information_schema.columns
                    WHERE table_name = 'menu_config_record'
                    ORDER BY ordinal_position;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            Console.WriteLine($"{reader.GetString(0)}: {reader.GetString(1)}");
        }
        return 0;
    }

    private static async Task<int> ExecSqlAsync(NpgsqlConnection conn, string path)
    {
        var content = await File.ReadAllTextAsync(path);
        await using var cmd = new NpgsqlCommand(content, conn);
        try
        {
            await cmd.ExecuteNonQueryAsync();
            Console.WriteLine($"OK: executed {path}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAILED: {ex.Message}");
            return 2;
        }
    }
}
