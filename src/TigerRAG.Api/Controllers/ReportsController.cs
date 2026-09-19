using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TigerRAG.Api.Common;
using TigerRAG.Application.Security;

namespace TigerRAG.Api.Controllers;

/// <summary>报表端点（骨架阶段）：列表与导出占位，全部受 <c>reports.view</c> 策略保护。</summary>
[ApiController]
[Route("api/reports")]
[Authorize(Roles = "Admin")]
public sealed class ReportsController : ControllerBase
{
    [HttpPost("list")]
    public IActionResult List() => Ok(ApiResponse<object?>.Success(null));

    [HttpPost("export")]
    public IActionResult Export() => Ok(ApiResponse<object?>.Success(null));
}
