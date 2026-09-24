using Microsoft.AspNetCore.Mvc;
using TigerRAG.Api.Common;
using TigerRAG.Application.Auth;
using TigerRAG.Application.KnowledgeBases;

namespace TigerRAG.Api.Controllers.KnowledgeBases;

/// <summary>知识库管理端点。所有方法仅做参数提取、授权校验、调用 <see cref="KnowledgeBaseService"/> 与异常映射。</summary>
[ApiController]
[Route("api/knowledge-bases")]
public sealed class KnowledgeBasesController(KnowledgeBaseService service) : ControllerBase
{
    /// <summary>查询当前用户可见的知识库列表；Admin 返回全部，非 Admin 仅返回 OwnerId == actor.Id。</summary>
    [HttpPost("list")]
    [MenuEndpoint("knowledgeBases", "knowledgeBases.list", "获取知识库列表")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<KnowledgeBaseDto>>>> List(CancellationToken cancellationToken)
    {
        if (!HttpContext.TryGetActor(out var actor) || actor is null)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Unauthorized, "用户身份无效"));
        }

        var isAdmin = User.IsInRole("Admin");
        try
        {
            var result = await service.ListAsync(actor, isAdmin, cancellationToken);
            return Ok(ApiResponse.Success(result));
        }
        catch (ArgumentException ex)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Validation, ex.Message));
        }
    }

    /// <summary>创建知识库。</summary>
    [HttpPost]
    [MenuEndpoint("knowledgeBases", "knowledgeBases.create", "创建知识库")]
    public async Task<ActionResult<ApiResponse<KnowledgeBaseDto>>> Create(
        [FromBody] CreateKnowledgeBaseRequest request,
        CancellationToken cancellationToken)
    {
        if (!HttpContext.TryGetActor(out var actor) || actor is null)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Unauthorized, "用户身份无效"));
        }

        try
        {
            var kb = await service.CreateAsync(request.Name, request.Description, actor, cancellationToken);
            return Ok(ApiResponse.Success(kb));
        }
        catch (ArgumentException ex)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Validation, ex.Message));
        }
    }

    /// <summary>获取知识库详情；非 Admin 必须是 Owner。</summary>
    [HttpPost("{kbId:guid}")]
    [MenuEndpoint("knowledgeBases", "knowledgeBases.get", "获取知识库详情")]
    public async Task<ActionResult<ApiResponse<KnowledgeBaseDto>>> Get(Guid kbId, CancellationToken cancellationToken)
    {
        if (!HttpContext.TryGetActor(out var actor) || actor is null)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Unauthorized, "用户身份无效"));
        }

        var isAdmin = User.IsInRole("Admin");
        try
        {
            var kb = await service.GetAsync(kbId, actor, isAdmin, cancellationToken);
            return Ok(ApiResponse.Success(kb));
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

    /// <summary>更新知识库元数据；非 Admin 必须是 Owner。</summary>
    [HttpPost("{kbId:guid}/update")]
    [MenuEndpoint("knowledgeBases", "knowledgeBases.update", "更新知识库")]
    public async Task<ActionResult<ApiResponse<KnowledgeBaseDto>>> Update(
        Guid kbId,
        [FromBody] UpdateKnowledgeBaseRequest request,
        CancellationToken cancellationToken)
    {
        if (!HttpContext.TryGetActor(out var actor) || actor is null)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Unauthorized, "用户身份无效"));
        }

        var isAdmin = User.IsInRole("Admin");
        try
        {
            var kb = await service.UpdateAsync(kbId, request, actor, isAdmin, cancellationToken);
            return Ok(ApiResponse.Success(kb));
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

    /// <summary>级联删除知识库（含文档/chunks/permissions/向量/MinIO 文件）。</summary>
    [HttpPost("{kbId:guid}/delete")]
    [MenuEndpoint("knowledgeBases", "knowledgeBases.delete", "删除知识库")]
    public async Task<ActionResult<ApiResponse<object?>>> Delete(Guid kbId, CancellationToken cancellationToken)
    {
        if (!HttpContext.TryGetActor(out var actor) || actor is null)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Unauthorized, "用户身份无效"));
        }

        var isAdmin = User.IsInRole("Admin");
        try
        {
            await service.DeleteAsync(kbId, actor, isAdmin, cancellationToken);
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

    /// <summary>批量删除知识库；请求体为 Guid 数组。</summary>
    [HttpPost("batch-delete")]
    [MenuEndpoint("knowledgeBases", "knowledgeBases.batchDelete", "批量删除知识库")]
    public async Task<ActionResult<ApiResponse<int>>> BatchDelete(
        [FromBody] BatchDeleteKnowledgeBasesRequest request,
        CancellationToken cancellationToken)
    {
        if (!HttpContext.TryGetActor(out var actor) || actor is null)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Unauthorized, "用户身份无效"));
        }

        var isAdmin = User.IsInRole("Admin");
        try
        {
            var count = await service.BatchDeleteAsync(request.Ids, actor, isAdmin, cancellationToken);
            return Ok(ApiResponse.Success(count, $"已删除 {count} 个知识库"));
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

    /// <summary>把 KB 下全部非 Processing 文档重置为 Pending 并重新入队。</summary>
    [HttpPost("{kbId:guid}/reindex")]
    [MenuEndpoint("knowledgeBases", "knowledgeBases.reindex", "重新索引知识库")]
    public async Task<ActionResult<ApiResponse<object?>>> Reindex(Guid kbId, CancellationToken cancellationToken)
    {
        if (!HttpContext.TryGetActor(out var actor) || actor is null)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Unauthorized, "用户身份无效"));
        }

        var isAdmin = User.IsInRole("Admin");
        try
        {
            await service.ReindexAsync(kbId, actor, isAdmin, cancellationToken);
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
    }
}