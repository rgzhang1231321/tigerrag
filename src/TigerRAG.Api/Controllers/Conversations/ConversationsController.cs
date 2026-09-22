using Microsoft.AspNetCore.Mvc;
using TigerRAG.Api.Common;
using TigerRAG.Application.Auth;

namespace TigerRAG.Api.Controllers.Conversations;

/// <summary>会话端点：创建会话、拉取消息、提交问答（流式走 SignalR Hub）。</summary>
[ApiController]
[Route("api/conversations")]
public sealed class ConversationsController : ControllerBase
{
    /// <summary>创建问答会话；当前骨架阶段尚未实现具体业务逻辑。</summary>
    /// <returns>当前返回业务码 50100，表示问答服务尚未实现。</returns>
    [HttpPost]
    [MenuEndpoint("conversations", "conversations.create", "创建问答会话")]
    public IActionResult Create() => Ok(ApiResponse<object?>.Failure(
        FlagStatesOption.NotImplemented,
        "问答服务尚未实现"));
}
