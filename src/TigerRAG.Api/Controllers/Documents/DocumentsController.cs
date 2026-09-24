using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using TigerRAG.Api.Common;
using TigerRAG.Application.Auth;
using TigerRAG.Application.Documents;

namespace TigerRAG.Api.Controllers.Documents;

/// <summary>文档管理端点：列表、上传、详情、删除、重索引。ACL 替换由 <see cref="DocumentPermissionsController"/> 单独承载。</summary>
[ApiController]
[Route("api/documents")]
public sealed class DocumentsController(
    DocumentService service) : ControllerBase
{
    private static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "text/markdown",
        "text/plain",
    };

    private const long MaxFileSizeBytes = 30L * 1024 * 1024;

    /// <summary>查询指定 KB 下当前用户可见的文档列表（ACL 过滤）。</summary>
    [HttpPost("list")]
    [MenuEndpoint("documents", "documents.list", "获取文档列表")]
    public async Task<ActionResult<ApiResponse<DocumentListPage>>> List(
        [FromBody] ListDocumentsRequest request,
        CancellationToken cancellationToken)
    {
        if (!HttpContext.TryGetActor(out var actor) || actor is null)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Unauthorized, "用户身份无效"));
        }

        var roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();
        var isAdmin = User.IsInRole("Admin");
        try
        {
            var page = await service.ListAsync(request.KbId, actor, roles, isAdmin, cancellationToken);
            return Ok(ApiResponse.Success(page));
        }
        catch (KeyNotFoundException ex)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.NotFound, ex.Message));
        }
    }

    /// <summary>上传文档到指定 KB（multipart/form-data：字段 <c>kbId</c>、<c>file</c>）。</summary>
    [HttpPost("upload")]
    [MenuEndpoint("documents", "documents.upload", "上传文档")]
    [RequestSizeLimit(MaxFileSizeBytes + 1024 * 1024)]
    public async Task<ActionResult<ApiResponse<DocumentDto>>> Upload(
        [FromForm] Guid kbId,
        [FromForm] IFormFile file,
        CancellationToken cancellationToken)
    {
        if (!HttpContext.TryGetActor(out var actor) || actor is null)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Unauthorized, "用户身份无效"));
        }

        if (file is null || file.Length == 0)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Validation, "文件不能为空。"));
        }
        if (file.Length > MaxFileSizeBytes)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Validation,
                $"文件大小 {file.Length} 字节超过 {MaxFileSizeBytes} 字节上限。"));
        }
        var mimeType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
        if (!AllowedMimeTypes.Contains(mimeType))
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Validation,
                $"不支持的 MIME 类型 {mimeType}。"));
        }

        await using var stream = file.OpenReadStream();
        var upload = new UploadDocumentRequest(kbId, file.FileName, mimeType, file.Length, stream);
        try
        {
            var doc = await service.UploadAsync(upload, actor, cancellationToken);
            return Ok(ApiResponse.Success(doc));
        }
        catch (ArgumentException ex)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Validation, ex.Message));
        }
        catch (KeyNotFoundException ex)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.NotFound, ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Forbidden, ex.Message));
        }
    }

    /// <summary>获取文档详情；非 Admin 必须在 ACL 可见集合内。</summary>
    [HttpPost("{documentId:guid}")]
    [MenuEndpoint("documents", "documents.get", "获取文档详情")]
    public async Task<ActionResult<ApiResponse<DocumentDto>>> Get(Guid documentId, CancellationToken cancellationToken)
    {
        if (!HttpContext.TryGetActor(out var actor) || actor is null)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Unauthorized, "用户身份无效"));
        }

        var roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();
        var isAdmin = User.IsInRole("Admin");
        try
        {
            var doc = await service.GetAsync(documentId, actor, roles, isAdmin, cancellationToken);
            return Ok(ApiResponse.Success(doc));
        }
        catch (KeyNotFoundException ex)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.NotFound, ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Forbidden, ex.Message));
        }
    }

    /// <summary>删除文档；级联清理 chunks/permissions/向量/MinIO。</summary>
    [HttpPost("{documentId:guid}/delete")]
    [MenuEndpoint("documents", "documents.delete", "删除文档")]
    public async Task<ActionResult<ApiResponse<object?>>> Delete(Guid documentId, CancellationToken cancellationToken)
    {
        if (!HttpContext.TryGetActor(out var actor) || actor is null)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Unauthorized, "用户身份无效"));
        }

        var roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();
        var isAdmin = User.IsInRole("Admin");
        try
        {
            await service.DeleteAsync(documentId, actor, roles, isAdmin, cancellationToken);
            return Ok(ApiResponse<object?>.Success(null));
        }
        catch (KeyNotFoundException ex)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.NotFound, ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Forbidden, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Conflict, ex.Message));
        }
    }

    /// <summary>批量删除文档；请求体为 Guid 数组。</summary>
    [HttpPost("batch-delete")]
    [MenuEndpoint("documents", "documents.batchDelete", "批量删除文档")]
    public async Task<ActionResult<ApiResponse<int>>> BatchDelete(
        [FromBody] BatchDeleteDocumentsRequest request,
        CancellationToken cancellationToken)
    {
        if (!HttpContext.TryGetActor(out var actor) || actor is null)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Unauthorized, "用户身份无效"));
        }

        var roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();
        var isAdmin = User.IsInRole("Admin");
        try
        {
            var count = await service.BatchDeleteAsync(request.Ids, actor, roles, isAdmin, cancellationToken);
            return Ok(ApiResponse.Success(count, $"已删除 {count} 个文档"));
        }
        catch (ArgumentException ex)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Validation, ex.Message));
        }
        catch (KeyNotFoundException ex)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.NotFound, ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Forbidden, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Conflict, ex.Message));
        }
    }

    /// <summary>获取文档预览内容（当前仅支持 text/plain，超出 1MB 截断）。</summary>
    [HttpPost("{documentId:guid}/content")]
    [MenuEndpoint("documents", "documents.content", "获取文档预览内容")]
    public async Task<ActionResult<ApiResponse<DocumentContentDto>>> GetContent(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        if (!HttpContext.TryGetActor(out var actor) || actor is null)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Unauthorized, "用户身份无效"));
        }

        var roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();
        var isAdmin = User.IsInRole("Admin");
        try
        {
            var content = await service.GetContentAsync(documentId, actor, roles, isAdmin, cancellationToken);
            return Ok(ApiResponse.Success(content));
        }
        catch (KeyNotFoundException ex)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.NotFound, ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Forbidden, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Validation, ex.Message));
        }
    }
}
