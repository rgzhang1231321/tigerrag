using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TigerRAG.Api.Common;
using TigerRAG.Application.Roles;
using TigerRAG.Application.Shared;

namespace TigerRAG.Api.Controllers.Roles;

[ApiController]
[Route("api/roles")]
[Authorize(Roles = "Admin")]
public sealed class RolesController(RoleAdminService roleAdmin) : ControllerBase
{
    /// <summary>列出全部角色；返回 <c>RoleDto</c>，含 IsSystem 标记（基于 SystemRoles.All 计算）。</summary>
    [HttpPost("list")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<RoleDto>>>> List(CancellationToken cancellationToken)
    {
        var roles = await roleAdmin.ListAsync(cancellationToken);
        return Ok(ApiResponse.Success(roles));
    }

    /// <summary>新建自定义角色。系统保留名或重名均返回 40000，DB 唯一索引冲突返回 40900。</summary>
    [HttpPost]
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

    /// <summary>删除自定义角色：系统保留名 / 格式非法返回 40000；角色不存在返回 40400；DAL 层数据库冲突返回 40900；删除后受影响用户的 stamp 已被轮换。</summary>
    [HttpPost("{name}/delete")]
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

    /// <summary>重命名角色：Admin 受保护返回 40000；新名格式非法 / 已被占用返回 40000；改名后受影响用户被强制下线。</summary>
    [HttpPost("{name}/rename")]
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
