using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TigerRAG.Api.Common;
using TigerRAG.Application.Security;

namespace TigerRAG.Api.Controllers;

/// <summary>知识库管理端点。骨架阶段仅占位，业务实现见 plan-day-work.md D1-D2。</summary>
[ApiController]
[Route("api/knowledge-bases")]
[Authorize(Roles = "Admin,KbManager")]
public sealed class KnowledgeBasesController : ControllerBase
{
    /// <summary>获取知识库列表；当前骨架阶段尚未实现具体业务逻辑。</summary>
    /// <returns>当前返回业务码 50100，表示知识库服务尚未实现。</returns>
    [HttpGet]
    public IActionResult List() => Ok(ApiResponse<object?>.Failure(
        FlagStatesOption.NotImplemented,
        "知识库服务尚未实现"));
}
