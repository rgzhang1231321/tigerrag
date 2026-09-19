using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
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

    /// <summary>平铺列表供管理页使用。需要 Admin 角色。</summary>
    [HttpPost("list")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MenuConfigItem>>>> List(CancellationToken cancellationToken)
    {
        var items = await userRoles.ListMenuAsync(cancellationToken);
        return Ok(ApiResponse.Success(items));
    }

    /// <summary>新建菜单配置。需要 Admin 角色。</summary>
    [HttpPost]
    [Authorize(Roles = "Admin")]
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
                request.Roles,
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

    /// <summary>更新菜单配置。需要 Admin 角色。</summary>
    [HttpPost("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ApiResponse<MenuConfigItem>>> Update(
        Guid id,
        UpdateMenuConfigApiRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            // 前端传 null = 不修改；传具体值 = 设置。Controller 负责翻译成 FieldUpdate 语义。
            // int/bool 为非空值类型，API 层用 nullable 表达"不修改"，此处转为 FieldUpdate。
            var item = await userRoles.UpdateMenuAsync(
                id,
                request.Label is null ? FieldUpdate<string>.Skip() : FieldUpdate<string>.Set(request.Label),
                request.Icon is null ? FieldUpdate<string>.Skip() : FieldUpdate<string>.Set(request.Icon),
                request.Roles is null ? FieldUpdate<IReadOnlyCollection<string>>.Skip() : FieldUpdate<IReadOnlyCollection<string>>.Set(request.Roles),
                request.ParentId is null ? FieldUpdate<Guid?>.Skip() : FieldUpdate<Guid?>.Set(request.ParentId),
                request.SortOrder is null ? FieldUpdate<int>.Skip() : FieldUpdate<int>.Set(request.SortOrder.Value),
                request.IsEnabled is null ? FieldUpdate<bool>.Skip() : FieldUpdate<bool>.Set(request.IsEnabled.Value),
                cancellationToken);
            return Ok(ApiResponse.Success(item, "菜单项已更新"));
        }
        catch (KeyNotFoundException)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.NotFound, "菜单项不存在"));
        }
        catch (ArgumentException error)
        {
            return Ok(ApiResponse<object?>.Failure(FlagStatesOption.Validation, error.Message));
        }
    }

    /// <summary>删除菜单配置及其全部后代。需要 Admin 角色。</summary>
    [HttpPost("{id:guid}/delete")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await userRoles.DeleteMenuAsync(id, cancellationToken);
        return deleted
            ? Ok(ApiResponse<object?>.Success(null, "菜单项已删除"))
            : Ok(ApiResponse<object?>.Failure(FlagStatesOption.NotFound, "菜单项不存在"));
    }
}

/// <summary>新建菜单配置请求。Roles 为空数组表示所有人可见。</summary>
public sealed record CreateMenuConfigRequest(
    string Key,
    string Label,
    string? Icon,
    string[] Roles,
    Guid? ParentId,
    int SortOrder,
    bool IsEnabled);

/// <summary>更新菜单配置请求：每个字段 null 表示"不修改"。</summary>
public sealed record UpdateMenuConfigApiRequest(
    string? Label,
    string? Icon,
    string[]? Roles,
    Guid? ParentId,
    int? SortOrder,
    bool? IsEnabled);
