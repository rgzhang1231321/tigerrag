using Microsoft.AspNetCore.Mvc;
using TigerRAG.Api.Common;
using TigerRAG.Application.Auth;

namespace TigerRAG.Api.Controllers.Roles;

/// <summary>角色-Endpoint 授权管理端点；通过 <c>[MenuEndpoint]</c> 统一授权，Admin 由全局 filter bypass。</summary>
[ApiController]
[Route("api/roles")]
public sealed class RoleEndpointGrantsController(
    RoleEndpointGrantService grantService,
    IMenuEndpointRegistry registry) : ControllerBase
{
    /// <summary>列出角色在全部已知 endpoint 上的授权矩阵（按菜单分组）。</summary>
    [HttpPost("{role}/grants")]
    [MenuEndpoint("roles", "roles.grants.list", "查询角色授权矩阵")]
    public async Task<ActionResult<ApiResponse<RoleEndpointMatrix>>> ListGrants(
        string role,
        CancellationToken cancellationToken)
    {
        try
        {
            var matrix = await grantService.ListForRoleAsync(role, registry.All, cancellationToken);
            return Ok(ApiResponse.Success(matrix));
        }
        catch (ArgumentException error)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Validation, error.Message));
        }
    }

    /// <summary>授予某个菜单下的全部 endpoint。</summary>
    [HttpPost("{role}/menus/{menuKey}/grant")]
    [MenuEndpoint("roles", "roles.grants.grantMenu", "授予菜单全部 endpoint")]
    public async Task<ActionResult<ApiResponse<int>>> GrantMenu(
        string role,
        string menuKey,
        CancellationToken cancellationToken)
    {
        if (!HttpContext.TryGetActor(out var actor))
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Unauthorized, "用户身份无效"));
        }

        try
        {
            var affected = await grantService.GrantMenuAsync(actor, role, menuKey, registry.All, cancellationToken);
            return Ok(ApiResponse.Success(affected, $"已授予 {affected} 个接口访问权"));
        }
        catch (ArgumentException error)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Validation, error.Message));
        }
    }

    /// <summary>撤销某个菜单下的全部 endpoint。</summary>
    [HttpPost("{role}/menus/{menuKey}/revoke")]
    [MenuEndpoint("roles", "roles.grants.revokeMenu", "撤销菜单全部 endpoint")]
    public async Task<ActionResult<ApiResponse<int>>> RevokeMenu(
        string role,
        string menuKey,
        CancellationToken cancellationToken)
    {
        if (!HttpContext.TryGetActor(out var actor))
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Unauthorized, "用户身份无效"));
        }

        try
        {
            var affected = await grantService.RevokeMenuAsync(actor, role, menuKey, cancellationToken);
            return Ok(ApiResponse.Success(affected, $"已撤销 {affected} 个接口访问权"));
        }
        catch (ArgumentException error)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Validation, error.Message));
        }
    }

    /// <summary>切换单个 endpoint 授权状态。</summary>
    [HttpPost("{role}/endpoints/toggle")]
    [MenuEndpoint("roles", "roles.grants.toggle", "切换单 endpoint 授权")]
    public async Task<ActionResult<ApiResponse<object?>>> ToggleEndpoint(
        string role,
        [FromBody] ToggleEndpointRequest request,
        CancellationToken cancellationToken)
    {
        if (!HttpContext.TryGetActor(out var actor))
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Unauthorized, "用户身份无效"));
        }

        try
        {
            await grantService.ToggleEndpointAsync(actor, role, request.EndpointKey, request.MenuKey, request.Grant, cancellationToken);
            return Ok(ApiResponse.Success<object?>(null));
        }
        catch (ArgumentException error)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Validation, error.Message));
        }
    }

    /// <summary>一次性应用角色在所有 endpoint 上的最终授权状态（替换式批量）。</summary>
    [HttpPost("{role}/grants/batch")]
    [MenuEndpoint("roles", "roles.grants.applyBatch", "批量应用角色授权")]
    public async Task<ActionResult<ApiResponse<int>>> ApplyBatch(
        string role,
        [FromBody] BatchGrantsRequest request,
        CancellationToken cancellationToken)
    {
        if (!HttpContext.TryGetActor(out var actor))
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Unauthorized, "用户身份无效"));
        }

        try
        {
            var affected = await grantService.ApplyBatchAsync(actor, role, request.Endpoints, cancellationToken);
            return Ok(ApiResponse.Success(affected, $"已应用 {affected} 项授权变更"));
        }
        catch (ArgumentException error)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Validation, error.Message));
        }
    }
}