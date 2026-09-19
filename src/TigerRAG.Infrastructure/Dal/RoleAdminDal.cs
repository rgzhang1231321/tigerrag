using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TigerRAG.Application.Security;
using TigerRAG.Infrastructure.Persistence;

namespace TigerRAG.Infrastructure.Dal;

/// <summary>
/// 角色管理 DAL：AspNetRoles 通过 <see cref="RoleManager{T}"/> 写入。
/// <c>IsSystem</c> 由 Application 层覆写，DAL 一律返回 false。
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
        return names.Select(name => new RoleDto(name, false)).ToArray();
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

        return new RoleDto(name, false);
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
}
