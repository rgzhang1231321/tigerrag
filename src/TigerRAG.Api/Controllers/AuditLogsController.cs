using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TigerRAG.Application.Security;

namespace TigerRAG.Api.Controllers;

[ApiController]
[Route("api/audit-logs")]
[Authorize(Policy = SystemPermissions.ReadAudit)]
public sealed class AuditLogsController : ControllerBase
{
    /// <summary>
    /// 获取审计日志列表；当前骨架阶段尚未实现具体业务逻辑。
    /// </summary>
    /// <returns>当前返回业务码 50100，表示审计服务尚未实现。</returns>
    [HttpGet]
    public IActionResult List() => Ok(ApiResponse<object?>.Failure(
        ApiErrorCodes.NotImplemented,
        "审计服务尚未实现"));
}
