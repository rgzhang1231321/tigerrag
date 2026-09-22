using Microsoft.AspNetCore.Mvc;
using TigerRAG.Api.Common;
using TigerRAG.Application.Auth;

namespace TigerRAG.Api.Controllers.KnowledgeBases;

/// <summary>知识库管理端点。骨架阶段仅占位</summary>
[ApiController]
[Route("api/knowledge-bases")]
public sealed class KnowledgeBasesController : ControllerBase
{
    /// <summary>获取知识库列表；当前骨架阶段尚未实现具体业务逻辑。</summary>
    /// <returns>当前返回业务码 50100，表示知识库服务尚未实现。</returns>
    [HttpGet]
    [MenuEndpoint("knowledgeBases", "knowledgeBases.list", "获取知识库列表")]
    public IActionResult List() => Ok(ApiResponse<object?>.Failure(
        FlagStatesOption.NotImplemented,
        "知识库服务尚未实现"));
}
