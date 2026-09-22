using Microsoft.AspNetCore.Mvc;
using TigerRAG.Api.Common;
using TigerRAG.Application.Auth;

namespace TigerRAG.Api.Controllers.Reports;

/// <summary>报表端点（骨架阶段）；通过 <c>[MenuEndpoint]</c> 统一授权，Admin 由全局 filter bypass。</summary>
[ApiController]
[Route("api/reports")]
public sealed class ReportsController : ControllerBase
{
    /// <summary>查询报表列表。</summary>
    [HttpPost("list")]
    [MenuEndpoint("reports", "reports.list", "查询报表列表")]
    public IActionResult List() => Ok(ApiResponse<object?>.Success(null));

    /// <summary>导出报表。</summary>
    [HttpPost("export")]
    [MenuEndpoint("reports", "reports.export", "导出报表")]
    public IActionResult Export() => Ok(ApiResponse<object?>.Success(null));
}