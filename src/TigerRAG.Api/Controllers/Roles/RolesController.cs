using Microsoft.AspNetCore.Mvc;
using TigerRAG.Api.Common;
using TigerRAG.Application.Auth;
using TigerRAG.Application.Roles;

namespace TigerRAG.Api.Controllers.Roles;

/// <summary>角色管理端点；通过 <c>[MenuEndpoint]</c> 统一授权。所有角色平等，无 Admin bypass。</summary>
[ApiController]
[Route("api/roles")]
public sealed class RolesController(RoleAdminService roleAdmin) : ControllerBase
{
    /// <summary>列出全部角色；返回 <c>RoleDto</c>，含用户引用数、菜单引用数与菜单名称列表。</summary>
    [HttpPost("list")]
    [MenuEndpoint("roles", "roles.list", "查询角色列表")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<RoleDto>>>> List(CancellationToken cancellationToken)
    {
        var roles = await roleAdmin.ListAsync(cancellationToken);
        return Ok(ApiResponse.Success(roles));
    }

    /// <summary>新建角色。格式非法或重名返回 40000，DB 唯一索引冲突返回 40900。</summary>
    [HttpPost]
    [MenuEndpoint("roles", "roles.create", "创建角色")]
    public async Task<ActionResult<ApiResponse<RoleDto>>> Create(
        CreateRoleRequest request,
        CancellationToken cancellationToken)
    {
        if (!HttpContext.TryGetActor(out var actor))
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Unauthorized, "用户身份无效"));
        }

        try
        {
            var role = await roleAdmin.CreateRoleAsync(actor, request, cancellationToken);
            return Ok(ApiResponse.Success(role, "角色创建成功"));
        }
        catch (ArgumentException error)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Validation, error.Message));
        }
        catch (InvalidOperationException error)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Conflict, error.Message));
        }
    }

    /// <summary>删除角色：格式非法返回 40000；角色不存在返回 40400；DAL 层数据库冲突返回 40900；删除后受影响用户的 stamp 已被轮换。</summary>
    [HttpPost("{name}/delete")]
    [MenuEndpoint("roles", "roles.delete", "删除角色")]
    public async Task<IActionResult> Delete(string name, CancellationToken cancellationToken)
    {
        if (!HttpContext.TryGetActor(out var actor))
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Unauthorized, "用户身份无效"));
        }

        try
        {
            var deleted = await roleAdmin.DeleteAsync(actor, name, cancellationToken);
            return deleted
                ? Ok(ApiResponse.Success((object?)null))
                : Ok(ApiResponse<object?>.Failure(FlagStatesOption.NotFound, $"角色 {name} 不存在"));
        }
        catch (ArgumentException error)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Validation, error.Message));
        }
        catch (InvalidOperationException error)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Conflict, error.Message));
        }
    }

    /// <summary>重命名角色：新名格式非法 / 已被占用返回 40000；改名后受影响用户被强制下线。</summary>
    [HttpPost("{name}/rename")]
    [MenuEndpoint("roles", "roles.rename", "重命名角色")]
    public async Task<ActionResult<ApiResponse<RoleDto>>> Rename(
        string name,
        RenameRoleRequest request,
        CancellationToken cancellationToken)
    {
        if (!HttpContext.TryGetActor(out var actor))
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Unauthorized, "用户身份无效"));
        }

        try
        {
            var role = await roleAdmin.RenameAsync(actor, name, request, cancellationToken);
            return Ok(ApiResponse.Success(role, "角色重命名成功"));
        }
        catch (ArgumentException error)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Validation, error.Message));
        }
        catch (InvalidOperationException error)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Conflict, error.Message));
        }
    }
}