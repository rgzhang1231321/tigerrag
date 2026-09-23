using Microsoft.EntityFrameworkCore;
using Npgsql;
using TigerRAG.Application.Auth;
using TigerRAG.Infrastructure.Persistence;
using TigerRAG.Infrastructure.Persistence.Entities.RoleEndpointGrants;

namespace TigerRAG.Infrastructure.Auth;

/// <summary>角色-Endpoint 授权 DAL：对 role_endpoint_record 表的 CRUD。</summary>
public sealed class RoleEndpointGrantStore(TigerRagDbContext dbContext) : IRoleEndpointGrantStore
{
    public async Task<IReadOnlyList<RoleEndpointGrant>> ListByRoleAsync(string roleName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await dbContext.RoleEndpointGrants
            .Where(g => g.RoleName == roleName)
            .OrderBy(g => g.MenuKey)
            .ThenBy(g => g.EndpointKey)
            .Select(g => new RoleEndpointGrant(g.RoleName, g.MenuKey, g.EndpointKey, g.GrantedAt, g.GrantedBy))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> HasGrantAsync(IEnumerable<string> userRoles, string endpointKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var roleSet = userRoles.ToHashSet(StringComparer.Ordinal);
        return await dbContext.RoleEndpointGrants
            .AnyAsync(g => g.EndpointKey == endpointKey && roleSet.Contains(g.RoleName), cancellationToken);
    }

    public async Task GrantAsync(string roleName, string menuKey, string endpointKey, Guid actorId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var existing = await dbContext.RoleEndpointGrants
            .FirstOrDefaultAsync(g => g.RoleName == roleName && g.EndpointKey == endpointKey, cancellationToken);
        if (existing is not null) return;   // 幂等

        dbContext.RoleEndpointGrants.Add(new role_endpoint_grant_record
        {
            RoleName = roleName,
            MenuKey = menuKey,
            EndpointKey = endpointKey,
            GrantedAt = DateTimeOffset.UtcNow,
            GrantedBy = actorId,
        });
        await SaveOrSwallowUniqueViolationAsync(cancellationToken);
    }

    public async Task RevokeAsync(string roleName, string endpointKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var existing = await dbContext.RoleEndpointGrants
            .FirstOrDefaultAsync(g => g.RoleName == roleName && g.EndpointKey == endpointKey, cancellationToken);
        if (existing is null) return;   // 不存在静默忽略

        dbContext.RoleEndpointGrants.Remove(existing);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> GrantAllInMenuAsync(
        string roleName,
        string menuKey,
        IReadOnlyCollection<MenuEndpointDescriptor> endpoints,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var existingKeys = await dbContext.RoleEndpointGrants
            .Where(g => g.RoleName == roleName && g.MenuKey == menuKey)
            .Select(g => g.EndpointKey)
            .ToHashSetAsync(cancellationToken);

        var toAdd = endpoints
            .Where(e => !existingKeys.Contains(e.EndpointKey))
            .Select(e => new role_endpoint_grant_record
            {
                RoleName = roleName,
                MenuKey = menuKey,
                EndpointKey = e.EndpointKey,
                GrantedAt = DateTimeOffset.UtcNow,
                GrantedBy = actorId,
            })
            .ToList();

        if (toAdd.Count == 0) return 0;

        dbContext.RoleEndpointGrants.AddRange(toAdd);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return toAdd.Count;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // 并发实例在我们 SELECT 后 INSERT 前先 commit 了同一组 (RoleName, EndpointKey) 行，
            // 整批 SaveChanges 撞 PK (23505)。把未提交的待写实体清出 ChangeTracker，避免后续
            // SELECT 看到脏状态；本次不再视为"新增"，返回 0。
            dbContext.ChangeTracker.Clear();
            return 0;
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation;

    private async Task SaveOrSwallowUniqueViolationAsync(CancellationToken cancellationToken)
    {
        // 并发 INSERT 撞 PK (23505) 视为幂等成功：另一并发实例已写入同一 (RoleName, EndpointKey)，
        // 本次 SELECT 时未看见的"缺失行"实际已被对方提交。清掉 ChangeTracker 防止脏状态污染后续操作。
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            dbContext.ChangeTracker.Clear();
        }
    }

    public async Task<int> RevokeAllInMenuAsync(string roleName, string menuKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var toRemove = await dbContext.RoleEndpointGrants
            .Where(g => g.RoleName == roleName && g.MenuKey == menuKey)
            .ToListAsync(cancellationToken);

        if (toRemove.Count == 0) return 0;

        dbContext.RoleEndpointGrants.RemoveRange(toRemove);
        await dbContext.SaveChangesAsync(cancellationToken);
        return toRemove.Count;
    }

    public async Task<int> ApplyBatchAsync(
        string roleName,
        IReadOnlyCollection<BatchEndpointChange> desiredEndpoints,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // 现状：按角色拉所有授权行（PK = (RoleName, EndpointKey)）。
        var current = await dbContext.RoleEndpointGrants
            .Where(g => g.RoleName == roleName)
            .Select(g => new { g.MenuKey, g.EndpointKey })
            .ToListAsync(cancellationToken);

        // 目标态：只关心 granted=true；未出现的现有行视为撤销目标。
        var desiredKeys = desiredEndpoints
            .Where(c => c.Granted)
            .Select(c => (Menu: c.MenuKey, Endpoint: c.EndpointKey))
            .ToHashSet();

        var currentKeySet = current.Select(c => (c.MenuKey, c.EndpointKey)).ToHashSet();

        var toRemove = current
            .Where(c => !desiredKeys.Contains((c.MenuKey, c.EndpointKey)))
            .Select(c => c.EndpointKey)
            .ToList();

        var toAdd = desiredEndpoints
            .Where(c => c.Granted && !currentKeySet.Contains((c.MenuKey, c.EndpointKey)))
            .Select(c => new role_endpoint_grant_record
            {
                RoleName = roleName,
                MenuKey = c.MenuKey,
                EndpointKey = c.EndpointKey,
                GrantedAt = DateTimeOffset.UtcNow,
                GrantedBy = actorId,
            })
            .ToList();

        if (toRemove.Count == 0 && toAdd.Count == 0) return 0;

        if (toRemove.Count > 0)
        {
            await dbContext.RoleEndpointGrants
                .Where(g => g.RoleName == roleName && toRemove.Contains(g.EndpointKey))
                .ExecuteDeleteAsync(cancellationToken);
        }
        if (toAdd.Count > 0)
        {
            dbContext.RoleEndpointGrants.AddRange(toAdd);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        return toRemove.Count + toAdd.Count;
    }
}