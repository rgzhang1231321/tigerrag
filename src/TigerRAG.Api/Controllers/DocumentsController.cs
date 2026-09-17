using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TigerRAG.Api.Common;
using TigerRAG.Application.Security;

namespace TigerRAG.Api.Controllers;

/// <summary>文档管理端点：详情、上传、列表、删除、权限编辑。</summary>
[ApiController]
[Route("api/documents")]
[Authorize(Policy = SystemPermissions.ManageDocuments)]
public sealed class DocumentsController(DocumentAccessService documentAccess) : ControllerBase
{
    /// <summary>获取指定文档；当前骨架阶段尚未实现具体业务逻辑。</summary>
    /// <param name="documentId">需要查询的文档标识。</param>
    /// <returns>当前返回业务码 50100，表示文档服务尚未实现。</returns>
    [HttpGet("{documentId:guid}")]
    public IActionResult GetDocument(Guid documentId) => Ok(ApiResponse<object?>.Failure(
        FlagStatesOption.NotImplemented,
        "文档服务尚未实现"));

    /// <summary>替换指定文档的用户和角色访问权限。</summary>
    /// <param name="documentId">需要修改权限的文档标识。</param>
    /// <param name="request">允许访问文档的用户标识和角色集合。</param>
    /// <param name="cancellationToken">用于取消当前请求的令牌。</param>
    /// <returns>更新成功时返回空数据；无权或文档不存在时返回对应非零业务码。</returns>
    [HttpPut("{documentId:guid}/permissions")]
    public async Task<IActionResult> ReplacePermissions(
        Guid documentId,
        ReplaceDocumentPermissionsRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Unauthorized, "用户身份无效"));
        }

        var isAdmin = User.IsInRole(SystemRoles.Admin);
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

public sealed record ReplaceDocumentPermissionsRequest(
    [Required] IReadOnlyCollection<Guid> UserIds,
    [Required] IReadOnlyCollection<string> Roles);
