using Microsoft.AspNetCore.Mvc;
using TigerRAG.Api.Common;
using TigerRAG.Application.ApiLogs;
using TigerRAG.Application.Auth;

namespace TigerRAG.Api.Controllers.ApiLogs;

/// <summary>日志查询端点；通过 <c>[MenuEndpoint]</c> 统一授权，Admin 由全局 filter bypass。</summary>
[ApiController]
[Route("api/logs")]
public sealed class ApiLogsController(ApiLogService apiLogs) : ControllerBase
{
    /// <summary>查询日志列表，支持按时间范围、级别、RequestId、关键词过滤 + 分页。</summary>
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