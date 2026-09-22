namespace TigerRAG.Api.Common;

/// <summary>
/// 分页响应的便捷工厂；让 <c>HasNextPage</c>/<c>Total</c> 不必每个 controller 手动填。
/// </summary>
public static class PaginatedApiResponse
{
    public static ApiResponse<IReadOnlyCollection<T>> Of<T>(
        IReadOnlyCollection<T> data,
        int total,
        bool hasNextPage,
        string message = "success") =>
        new(string.Empty, FlagStatesOption.Success, nameof(FlagStatesOption.Success), true, message, data, hasNextPage, total);
}