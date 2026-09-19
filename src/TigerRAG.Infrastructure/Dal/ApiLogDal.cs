using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;
using TigerRAG.Application.Security;

namespace TigerRAG.Infrastructure.Dal;

/// <summary>日志查询 DAL：用 Dapper 从 api_log 表读取日志条目。与写入端保持一致，绕过 EF Core。</summary>
public sealed class ApiLogDal(IConfiguration configuration) : IApiLogDal
{
    /// <summary>按条件查询日志条目，返回分页结果。</summary>
    public async Task<ApiLogQueryResult> ListAsync(ApiLogQueryRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var where = BuildWhereClause(request);
        var parameters = BuildParameters(request);

        using var connection = new NpgsqlConnection(ResolveConnectionString());
        connection.Open();

        var totalSql = $"SELECT COUNT(*) FROM api_log {where}";
        var total = await connection.ExecuteScalarAsync<int>(totalSql, parameters);

        var entriesSql = $"""
            SELECT id, timestamp, level, request_id, source_context, request_path, message, exception, elapsed_ms
            FROM api_log
            {where}
            ORDER BY timestamp DESC
            LIMIT @PageSize OFFSET @Offset
            """;
        var entries = (await connection.QueryAsync<ApiLogEntryDto>(entriesSql, parameters)).ToList();

        return new ApiLogQueryResult(entries, total);
    }

    private static string BuildWhereClause(ApiLogQueryRequest request)
    {
        var conditions = new List<string>();
        if (request.From.HasValue)
            conditions.Add("timestamp >= @From");
        if (request.To.HasValue)
            conditions.Add("timestamp <= @To");
        if (!string.IsNullOrWhiteSpace(request.Level))
            conditions.Add("level = @Level");
        if (!string.IsNullOrWhiteSpace(request.RequestId))
            conditions.Add("request_id = @RequestId");
        if (!string.IsNullOrWhiteSpace(request.Keyword))
            conditions.Add("message ILIKE @Keyword");

        return conditions.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", conditions);
    }

    private static object BuildParameters(ApiLogQueryRequest request) => new
    {
        request.From,
        request.To,
        Level = request.Level,
        RequestId = request.RequestId,
        Keyword = string.IsNullOrWhiteSpace(request.Keyword) ? null : $"%{request.Keyword}%",
        PageSize = request.PageSize,
        Offset = (request.Page - 1) * request.PageSize,
    };

    private string ResolveConnectionString() =>
        configuration.GetConnectionString("Logging")
            ?? configuration.GetConnectionString("PostgreSql")
            ?? throw new InvalidOperationException("ApiLogDal requires ConnectionStrings:Logging or ConnectionStrings:PostgreSql.");
}
