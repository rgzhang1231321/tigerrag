using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using TigerRAG.Application.Shared;

namespace TigerRAG.Api.Common;

/// <summary>从 HttpContext 提取当前操作人信息。</summary>
public static class ActorContextExtensions
{
    /// <summary>
    /// 从 JWT claims 提取 ActorContext。<c>ClaimTypes.NameIdentifier</c> 存用户 Id，
    /// <c>ClaimTypes.Name</c> 存用户名；两者均由 <c>JwtAccessTokenIssuer</c> 写入。
    /// Guid 解析失败返回 false。
    /// </summary>
    public static bool TryGetActor(this HttpContext context, out ActorContext? actor)
    {
        var idClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var nameClaim = context.User.FindFirst(ClaimTypes.Name)?.Value;

        if (idClaim is null || nameClaim is null || !Guid.TryParse(idClaim, out var userId))
        {
            actor = null;
            return false;
        }

        actor = new ActorContext(userId, nameClaim);
        return true;
    }
}
