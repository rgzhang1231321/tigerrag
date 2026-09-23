using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using TigerRAG.Api.Common;
using TigerRAG.Application.Auth;
using TigerRAG.Application.Documents;

namespace TigerRAG.Api.Controllers.Documents;

/// <summary>文档 ACL 替换端点。仅依赖 <see cref="DocumentAccessService"/>，不触发 <c>DocumentService</c> 的基础设施依赖（MinIO/Redis/Qdrant），保证单测与集成测试环境在 MinIO 未配置时仍可访问权限端点。</summary>
[ApiController]
[Route("api/documents")]
public sealed class DocumentPermissionsController(
    DocumentAccessService documentAccess) : ControllerBase
{
    /// <summary>替换指定文档的用户和角色访问权限。</summary>
    [HttpPost("{documentId:guid}/permissions")]
    [MenuEndpoint("documents", "documents.permissions.replace", "替换文档权限")]
    public async Task<IActionResult> ReplacePermissions(
        Guid documentId,
        ReplaceDocumentPermissionsRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Unauthorized, "用户身份无效"));
        }

        var isAdmin = User.IsInRole("Admin");
        try
        {
            await documentAccess.ReplacePermissionsAsync(
                documentId,
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
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.NotFound, "文档不存在"));
        }
    }
}
