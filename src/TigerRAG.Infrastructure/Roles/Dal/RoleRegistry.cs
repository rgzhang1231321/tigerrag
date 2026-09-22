using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TigerRAG.Application.Roles;
using TigerRAG.Infrastructure.Persistence;

namespace TigerRAG.Infrastructure.Roles.Dal;

/// <summary>基于 <see cref="TigerRagDbContext"/> 的角色注册表实现；查询通过 NormalizedName 大小写不敏感匹配，复用 Identity 的归一化规则。</summary>
public sealed class RoleRegistry(TigerRagDbContext dbContext) : IRoleRegistry
{
    public Task<bool> RoleExistsAsync(string name, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(name))
        {
            return Task.FromResult(false);
        }
        var normalized = name.ToUpperInvariant();
        return dbContext.Roles.AnyAsync(role => role.NormalizedName == normalized, cancellationToken);
    }

    public async Task<bool> UserHasRoleAsync(Guid userId, string name, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }
        var normalized = name.ToUpperInvariant();
        return await dbContext.UserRoles
            .Where(ur => ur.UserId == userId)
            .Join(dbContext.Roles, ur => ur.RoleId, role => role.Id, (ur, role) => role)
            .AnyAsync(role => role.NormalizedName == normalized, cancellationToken);
    }

    public async Task<int> CountHoldersAsync(string name, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(name))
        {
            return 0;
        }
        var normalized = name.ToUpperInvariant();
        var roleId = await dbContext.Roles
            .Where(role => role.NormalizedName == normalized)
            .Select(role => (Guid?)role.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (roleId is null)
        {
            return 0;
        }
        return await dbContext.UserRoles
            .Where(ur => ur.RoleId == roleId.Value)
            .CountAsync(cancellationToken);
    }
}
