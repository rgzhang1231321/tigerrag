using System.Text;
using Microsoft.AspNetCore.Mvc;
using TigerRAG.Api.Common;
using TigerRAG.Application.Auth;
using TigerRAG.Application.Statistics;
using TigerRAG.Application.Statistics.Dashboard;
using TigerRAG.Application.Statistics.Reports;
using SysText = System.Text;

namespace TigerRAG.Api.Controllers.Statistics;

/// <summary>Dashboard 统计：单端点并行聚合核心指标，避免前端 N+1；通过 <c>[MenuEndpoint]</c> 统一授权。</summary>
[ApiController]
[Route("api/statistics")]
public sealed class StatisticsController(IStatisticsService statisticsService) : ControllerBase
{
    /// <summary>聚合 Dashboard 所需的核心指标和趋势数据。</summary>
    [HttpPost("dashboard")]
    [MenuEndpoint("statistics", "statistics.dashboard", "查询 Dashboard 聚合指标")]
    public async Task<ActionResult<ApiResponse<DashboardMetricsResponse>>> GetDashboard(
        CancellationToken cancellationToken)
    {
        var metrics = await statisticsService.GetDashboardMetricsAsync(cancellationToken);
        return Ok(ApiResponse.Success(metrics));
    }

    /// <summary>获取指定类型和日期范围的报表数据。</summary>
    [HttpPost("reports")]
    [MenuEndpoint("statistics", "statistics.reports", "查询报表数据")]
    public async Task<ActionResult<ApiResponse<ReportDataWrapper>>> GetReport(
        [FromBody] ReportRequest request,
        CancellationToken cancellationToken)
    {
        var data = await statisticsService.GetReportAsync(request, cancellationToken);
        return Ok(ApiResponse.Success(data));
    }

    /// <summary>导出报表为 CSV 文件。</summary>
    [HttpPost("reports/export")]
    [MenuEndpoint("statistics", "statistics.reports.export", "导出报表 CSV")]
    public async Task<IActionResult> ExportReport(
        [FromBody] ReportRequest request,
        CancellationToken cancellationToken)
    {
        var csv = await statisticsService.ExportReportAsync(request, cancellationToken);
        var fileName = $"report_{request.ReportType}_{DateTime.UtcNow:yyyyMMdd}.csv";
        return File(SysText.Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8", fileName);
    }
}