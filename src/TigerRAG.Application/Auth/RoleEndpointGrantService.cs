using TigerRAG.Application.OperationAudit;
using TigerRAG.Application.Shared;

namespace TigerRAG.Application.Auth;

/// <summary>角色-Endpoint 授权用例：按菜单批量授权/撤销、单 endpoint 切换、矩阵查询。所有角色平等可配置，无 Admin 例外。</summary>
public sealed class RoleEndpointGrantService(
    IRoleEndpointGrantStore store,
    IOperationAuditWriter auditWriter,
    IUnitOfWork unitOfWork)
{
    /// <summary>列出角色在全部已知 endpoint 上的授权矩阵（按菜单分组）。</summary>
    public async Task<RoleEndpointMatrix> ListForRoleAsync(
        string roleName,
        IReadOnlyCollection<MenuEndpointDescriptor> allEndpoints,
        CancellationToken cancellationToken)
    {
        var grants = await store.ListByRoleAsync(roleName, cancellationToken);
        var grantedKeys = grants.Select(g => g.EndpointKey).ToHashSet(StringComparer.Ordinal);

        var groups = allEndpoints
            .GroupBy(ep => ep.MenuKey, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new MenuGroup(
                g.Key,
                g.OrderBy(ep => ep.EndpointKey, StringComparer.Ordinal)
                 .Select(ep => new EndpointGrantView(
                     ep.EndpointKey, ep.Description, ep.HttpMethod, ep.Path,
                     grantedKeys.Contains(ep.EndpointKey)))
                 .ToList()))
            .ToList();

        return new RoleEndpointMatrix(groups);
    }

    /// <summary>授予某个菜单下的全部 endpoint；返回实际新增行数。</summary>
    public async Task<int> GrantMenuAsync(
        ActorContext actor,
        string roleName,
        string menuKey,
        IReadOnlyCollection<MenuEndpointDescriptor> allEndpoints,
        CancellationToken cancellationToken)
    {
        var menuEndpoints = allEndpoints.Where(ep => ep.MenuKey == menuKey).ToList();
        if (menuEndpoints.Count == 0) return 0;

        var affected = 0;
        await unitOfWork.ExecuteAsync(async innerCt =>
        {
            affected = await store.GrantAllInMenuAsync(roleName, menuKey, menuEndpoints, actor.Id, innerCt);

            await auditWriter.RecordAsync(new OperationAuditEntry(
                actor.Id,
                actor.Name,
                OperationAuditActions.RoleEndpointGrantAll,
                "role_endpoint",
                $"{roleName}/{menuKey}",
                $"{actor.Name} 授予角色 {roleName} 在菜单 {menuKey} 下的全部 {menuEndpoints.Count} 个接口访问权"), innerCt);
        }, cancellationToken);

        return affected;
    }

    /// <summary>撤销某个菜单下的全部 endpoint；返回实际删除行数。</summary>
    public async Task<int> RevokeMenuAsync(
        ActorContext actor,
        string roleName,
        string menuKey,
        CancellationToken cancellationToken)
    {
        var affected = 0;
        await unitOfWork.ExecuteAsync(async innerCt =>
        {
            affected = await store.RevokeAllInMenuAsync(roleName, menuKey, innerCt);

            await auditWriter.RecordAsync(new OperationAuditEntry(
                actor.Id,
                actor.Name,
                OperationAuditActions.RoleEndpointRevokeAll,
                "role_endpoint",
                $"{roleName}/{menuKey}",
                $"{actor.Name} 撤销角色 {roleName} 在菜单 {menuKey} 下的全部接口访问权"), innerCt);
        }, cancellationToken);

        return affected;
    }

    /// <summary>切换单个 endpoint 授权状态。<paramref name="grant"/>=true 授予；false 撤销。</summary>
    public async Task ToggleEndpointAsync(
        ActorContext actor,
        string roleName,
        string endpointKey,
        string menuKey,
        bool grant,
        CancellationToken cancellationToken)
    {
        await unitOfWork.ExecuteAsync(async innerCt =>
        {
            if (grant)
                await store.GrantAsync(roleName, menuKey, endpointKey, actor.Id, innerCt);
            else
                await store.RevokeAsync(roleName, endpointKey, innerCt);

            await auditWriter.RecordAsync(new OperationAuditEntry(
                actor.Id,
                actor.Name,
                OperationAuditActions.RoleEndpointToggle,
                "role_endpoint",
                $"{roleName}/{endpointKey}",
                $"{actor.Name} {(grant ? "授予" : "撤销")} 角色 {roleName} 的 {endpointKey} 接口访问权"), innerCt);
        }, cancellationToken);
    }

    /// <summary>单事务一次性应用角色在全部 endpoint 上的最终授权；返回实际 grant+revoke 行数。无差异时不写审计。</summary>
    public async Task<int> ApplyBatchAsync(
        ActorContext actor,
        string roleName,
        IReadOnlyCollection<BatchEndpointChange> desiredEndpoints,
        CancellationToken cancellationToken)
    {
        var affected = 0;
        await unitOfWork.ExecuteAsync(async innerCt =>
        {
            affected = await store.ApplyBatchAsync(roleName, desiredEndpoints, actor.Id, innerCt);
            if (affected == 0) return;

            await auditWriter.RecordAsync(new OperationAuditEntry(
                actor.Id,
                actor.Name,
                OperationAuditActions.RoleEndpointApplyBatch,
                "role_endpoint",
                roleName,
                $"{actor.Name} 批量应用角色 {roleName} 的授权变更，合计 {affected} 项"), innerCt);
        }, cancellationToken);
        return affected;
    }
}