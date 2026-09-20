using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TigerRAG.Application.Security;
using TigerRAG.Infrastructure.Persistence;

namespace TigerRAG.Infrastructure.Dal;

/// <summary>
/// 角色管理 DAL：AspNetRoles 通过 <see cref="RoleManager{T}"/> 写入。
/// <c>IsSystem</c> 与引用计数由 Application 层组装（依赖 <see cref="IRoleMenuReference"/> 与自身 <c>CountAssignmentsAsync</c>），DAL 仅返回原始 name 列表与基础写操作。
/// </summary>
public sealed class RoleAdminDal(
    RoleManager<IdentityRole<Guid>> roleManager,
    TigerRagDbContext dbContext) : IRoleAdmin
{
    public async Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var names = await dbContext.Roles
            .OrderBy(role => role.Name)
            .Select(role => role.Name ?? string.Empty)
            .ToListAsync(cancellationToken);
        // DAL 一律返回 IsSystem=false / 引用计数 0 / 空菜单列表；由 Application 层通过其它端口填实。
        return names.Select(name => new RoleDto(name, false, 0, 0, Array.Empty<string>())).ToArray();
    }

    public Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return roleManager.RoleExistsAsync(name);
    }

    public async Task<RoleDto> CreateRoleAsync(string name, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var identityRole = new IdentityRole<Guid>(name);
        var result = await roleManager.CreateAsync(identityRole);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"创建角色 {name} 失败：{string.Join("; ", result.Errors.Select(error => error.Description))}");
        }

        return new RoleDto(name, false, 0, 0, Array.Empty<string>());
    }

    public async Task<int> CountAssignmentsAsync(string name, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var role = await roleManager.FindByNameAsync(name);
        if (role is null)
        {
            return 0;
        }

        return await dbContext.UserRoles.CountAsync(ur => ur.RoleId == role.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ListAssignedUserIdsAsync(string name, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var role = await roleManager.FindByNameAsync(name);
        if (role is null)
        {
            return Array.Empty<Guid>();
        }

        return await dbContext.UserRoles
            .Where(ur => ur.RoleId == role.Id)
            .Select(ur => ur.UserId)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(string name, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var role = await roleManager.FindByNameAsync(name);
        if (role is null)
        {
            return false;
        }

        var result = await roleManager.DeleteAsync(role);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"删除角色 {name} 失败：{string.Join("; ", result.Errors.Select(error => error.Description))}");
        }

        return true;
    }

    public async Task<bool> RenameAsync(string oldName, string newName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var role = await roleManager.FindByNameAsync(oldName);
        if (role is null)
        {
            throw new InvalidOperationException($"角色 {oldName} 不存在。");
        }

        role.Name = newName;
        var result = await roleManager.UpdateAsync(role);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"重命名角色 {oldName} → {newName} 失败：{string.Join("; ", result.Errors.Select(error => error.Description))}");
        }
        return true;
    }
}
