using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TigerRAG.Api.Common;
using TigerRAG.Application.Security;

namespace TigerRAG.Api.Controllers;

/// <summary>审计日志查询端点。仅 Auditor 与 Admin 通过 Policy 准入。</summary>
[ApiController]
[Route("api/audit-logs")]
[Authorize(Policy = SystemPermissions.ReadAudit)]
public sealed class AuditLogsController : ControllerBase
{
    /// <summary>获取审计日志列表；当前骨架阶段尚未实现具体业务逻辑。</summary>
    /// <returns>当前返回业务码 50100，表示审计服务尚未实现。</returns>
    [HttpPost("list")]
    public IActionResult List() => Ok(ApiResponse<object?>.Failure(
        FlagStatesOption.NotImplemented,
        "审计服务尚未实现"));
}
