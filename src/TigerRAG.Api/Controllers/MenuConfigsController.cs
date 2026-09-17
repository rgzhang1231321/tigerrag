using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TigerRAG.Api.Common;
using TigerRAG.Application.Security;

namespace TigerRAG.Api.Controllers;

[ApiController]
[Route("api/menu-configs")]
[Authorize]
public sealed class MenuConfigsController(UserRoleService userRoles) : ControllerBase
{
    /// <summary>获取前端导航用的菜单配置（全部启用项，平铺）。前端据此自行组装树形导航。</summary>
    [HttpPost("tree")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MenuConfigItem>>>> Tree(CancellationToken cancellationToken)
    {
        var items = await userRoles.ListMenuAsync(cancellationToken);
        var enabled = items.Where(item => item.IsEnabled).ToArray();
        return Ok(ApiResponse.Success(enabled));
    }

    /// <summary>平铺列表供管理页使用。需要 users.manage 权限。</summary>
    [HttpPost("list")]
    [Authorize(Policy = SystemPermissions.ManageUsers)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MenuConfigItem>>>> List(CancellationToken cancellationToken)
    {
        var items = await userRoles.ListMenuAsync(cancellationToken);
        return Ok(ApiResponse.Success(items));
    }

    /// <summary>新建菜单配置。需要 users.manage 权限。</summary>
    [HttpPost]
    [Authorize(Policy = SystemPermissions.ManageUsers)]
    public async Task<ActionResult<ApiResponse<MenuConfigItem>>> Create(
        CreateMenuConfigRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var item = await userRoles.CreateMenuAsync(
                request.Key,
                request.Label,
                request.Icon,
                request.Permission,
                request.ParentId,
                request.SortOrder,
                request.IsEnabled,
                cancellationToken);
            return Ok(ApiResponse.Success(item, "菜单项已创建"));
        }
        catch (ArgumentException error)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Validation, error.Message));
        }
    }

    /// <summary>更新菜单配置。需要 users.manage 权限。</summary>
    [HttpPost("{id:guid}")]
    [Authorize(Policy = SystemPermissions.ManageUsers)]
    public async Task<ActionResult<ApiResponse<MenuConfigItem>>> Update(
        Guid id,
        UpdateMenuConfigRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var item = await userRoles.UpdateMenuAsync(
                id,
                request.Label,
                request.Icon,
                request.Permission,
                request.ParentId,
                request.SortOrder,
                request.IsEnabled,
                cancellationToken);
            return Ok(ApiResponse.Success(item, "菜单项已更新"));
        }
        catch (KeyNotFoundException)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.NotFound, "菜单项不存在"));
        }
    }

    /// <summary>删除菜单配置（级联删除子项）。需要 users.manage 权限。</summary>
    [HttpPost("{id:guid}/delete")]
    [Authorize(Policy = SystemPermissions.ManageUsers)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await userRoles.DeleteMenuAsync(id, cancellationToken);
        return deleted
            ? Ok(ApiResponse<object?>.Success(null, "菜单项已删除"))
            : Ok(ApiResponse<object?>.Failure(FlagStatesOption.NotFound, "菜单项不存在"));
    }

}

public sealed record CreateMenuConfigRequest(
    string Key,
    string Label,
    string? Icon,
    string? Permission,
    Guid? ParentId,
    int SortOrder,
    bool IsEnabled);

public sealed record UpdateMenuConfigRequest(
    string? Label,
    string? Icon,
    string? Permission,
    Guid? ParentId,
    int? SortOrder,
    bool? IsEnabled);