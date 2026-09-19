using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TigerRAG.Api.Common;
using TigerRAG.Application.Security;

namespace TigerRAG.Api.Controllers;

/// <summary>日志查询端点。仅 Admin 可访问。</summary>
[ApiController]
[Route("api/logs")]
[Authorize(Roles = "Admin")]
public sealed class ApiLogsController(ApiLogService apiLogs) : ControllerBase
{
    /// <summary>查询日志列表，支持按时间范围、级别、RequestId、关键词过滤 + 分页。</summary>
    [HttpPost("list")]
    public async Task<ActionResult<ApiResponse<ApiLogQueryResult>>> List(
        ApiLogQueryRequest request,
        CancellationToken cancellationToken)
    {
        var result = await apiLogs.ListAsync(request, cancellationToken);
        return Ok(ApiResponse.Success(result));
    }
}
