using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TigerRAG.Api.Common;
using TigerRAG.Application.OperationAudit;

namespace TigerRAG.Api.Controllers.OperationAudit;

/// <summary>审计日志查询端点。仅 Auditor 与 Admin 通过 Policy 准入。</summary>
[ApiController]
[Route("api/audit-logs")]
[Authorize(Roles = "Admin,Auditor")]
public sealed class AuditLogsController(IOperationAuditDal auditDal) : ControllerBase
{
    /// <summary>查询操作审计日志，支持按操作人、操作类型、时间范围和关键词过滤。</summary>
    /// <param name="request">查询条件与分页参数。</param>
    /// <param name="cancellationToken">用于取消当前请求的令牌。</param>
    /// <returns>分页的审计日志列表。</returns>
    [HttpPost("list")]
    public async Task<ActionResult<ApiResponse<OperationAuditQueryResult>>> List(
        [FromBody] OperationAuditQueryRequest request,
        CancellationToken cancellationToken)
    {
        if (!HttpContext.TryGetActor(out var actor) || actor is null)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Unauthorized, "用户身份无效"));
        }

        var result = await auditDal.QueryAsync(
            actor,
            request,
            cancellationToken);

        return Ok(ApiResponse.Success(result));
    }
}
