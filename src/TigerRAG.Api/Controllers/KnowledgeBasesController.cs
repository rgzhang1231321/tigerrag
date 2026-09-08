using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TigerRAG.Application.Security;

namespace TigerRAG.Api.Controllers;

[ApiController]
[Route("api/knowledge-bases")]
[Authorize(Policy = SystemPermissions.ManageKnowledgeBases)]
public sealed class KnowledgeBasesController : ControllerBase
{
    /// <summary>
    /// 获取知识库列表；当前骨架阶段尚未实现具体业务逻辑。
    /// </summary>
    /// <returns>当前返回业务码 50100，表示知识库服务尚未实现。</returns>
    [HttpGet]
    public IActionResult List() => Ok(ApiResponse<object?>.Failure(
        ApiErrorCodes.NotImplemented,
        "知识库服务尚未实现"));
}
