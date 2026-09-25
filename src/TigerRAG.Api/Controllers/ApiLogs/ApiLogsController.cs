using Microsoft.AspNetCore.Mvc;
using TigerRAG.Api.Common;
using TigerRAG.Application.ApiLogs;
using TigerRAG.Application.Auth;

namespace TigerRAG.Api.Controllers.ApiLogs;

/// <summary>日志查询端点（消息日志与访问日志共用，按 kind 过滤）；
/// 通过 <c>[MenuEndpoint]</c> 统一授权，Admin 由全局 filter bypass。</summary>
[ApiController]
[Route("api/logs")]
public sealed class ApiLogsController(ApiLogService apiLogs) : ControllerBase
{
    /// <summary>查询日志列表：kind='message' 走消息筛选（级别/关键词），kind='access' 走访问筛选（用户名/路径/状态码）。</summary>
    [HttpPost("list")]
    [MenuEndpoint("apiLogs", "apiLogs.list", "查询 API 日志列表")]
    public async Task<ActionResult<ApiResponse<ApiLogQueryResult>>> List(
        ApiLogQueryRequest request,
        CancellationToken cancellationToken)
    {
        var result = await apiLogs.ListAsync(request, cancellationToken);
        return Ok(ApiResponse.Success(result));
    }
}
