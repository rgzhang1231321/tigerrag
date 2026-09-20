using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TigerRAG.Api.Common;
using TigerRAG.Application.Security;

namespace TigerRAG.Api.Controllers;

/// <summary>系统概览 Dashboard 统计：单端点并行聚合核心指标，避免前端 N+1。</summary>
[ApiController]
[Route("api/statistics")]
[Authorize]
public sealed class StatisticsController(IStatisticsService statisticsService) : ControllerBase
{
    /// <summary>聚合 Dashboard 所需的核心指标和趋势数据。</summary>
    [HttpPost("dashboard")]
    public async Task<ActionResult<ApiResponse<DashboardMetricsResponse>>> GetDashboard(
        CancellationToken cancellationToken)
    {
        var metrics = await statisticsService.GetDashboardMetricsAsync(cancellationToken);
        return Ok(ApiResponse.Success(metrics));
    }
}
