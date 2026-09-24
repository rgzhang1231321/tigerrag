using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using TigerRAG.Api.Common;
using TigerRAG.Application.Auth;
using TigerRAG.Application.KnowledgeBases;

namespace TigerRAG.Api.Controllers.KnowledgeBases;

/// <summary>KB ACL 替换端点。仅依赖 <see cref="KnowledgeBaseAccessService"/>，不触发 MinIO/Redis/Qdrant 依赖，保证单测与集成测试环境在 MinIO 未配置时仍可访问权限端点。</summary>
[ApiController]
[Route("api/knowledge-bases")]
public sealed class KnowledgeBasePermissionsController(
    KnowledgeBaseAccessService kbAccess) : ControllerBase
{
    /// <summary>获取指定 KB 的当前 ACL 状态；授权规则同 ReplacePermissions。</summary>
    [HttpGet("{kbId:guid}/permissions")]
    [MenuEndpoint("knowledgeBases", "knowledgeBases.permissions.get", "查询知识库权限")]
    public async Task<IActionResult> GetPermissions(
        Guid kbId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Unauthorized, "用户身份无效"));
        }

        var isAdmin = User.IsInRole("Admin");
        try
        {
            var permissions = await kbAccess.GetPermissionsAsync(
                kbId,
                actorId,
                isAdmin,
                cancellationToken);
            return Ok(ApiResponse<object?>.Success(permissions));
        }
        catch (KeyNotFoundException)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.NotFound, "知识库不存在"));
        }
        catch (UnauthorizedAccessException error)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Forbidden, error.Message));
        }
    }

    /// <summary>替换指定 KB 的用户和角色访问权限。</summary>
    [HttpPost("{kbId:guid}/permissions")]
    [MenuEndpoint("knowledgeBases", "knowledgeBases.permissions.replace", "替换知识库权限")]
    public async Task<IActionResult> ReplacePermissions(
        Guid kbId,
        ReplaceKbPermissionsRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Unauthorized, "用户身份无效"));
        }

        var isAdmin = User.IsInRole("Admin");
        try
        {
            await kbAccess.ReplacePermissionsAsync(
                kbId,
                actorId,
                isAdmin,
                request.UserIds,
                request.Roles,
                cancellationToken);
            return Ok(ApiResponse<object?>.Success(null));
        }
        catch (ArgumentException error)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Validation, error.Message));
        }
        catch (UnauthorizedAccessException error)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Forbidden, error.Message));
        }
        catch (KeyNotFoundException)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.NotFound, "知识库不存在"));
        }
    }
}
