using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TigerRAG.Application.Security;

namespace TigerRAG.Api.Controllers;

[ApiController]
[Route("api/conversations")]
[Authorize(Policy = SystemPermissions.UseChat)]
public sealed class ConversationsController : ControllerBase
{
    /// <summary>
    /// 创建问答会话；当前骨架阶段尚未实现具体业务逻辑。
    /// </summary>
    /// <returns>当前返回业务码 50100，表示问答服务尚未实现。</returns>
    [HttpPost]
    public IActionResult Create() => Ok(ApiResponse<object?>.Failure(
        ApiErrorCodes.NotImplemented,
        "问答服务尚未实现"));
}
