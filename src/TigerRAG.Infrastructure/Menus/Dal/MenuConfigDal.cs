using Microsoft.EntityFrameworkCore;
using TigerRAG.Application.Menus;
using TigerRAG.Application.Shared;
using TigerRAG.Infrastructure.Persistence;
using TigerRAG.Infrastructure.Persistence.Entities.Menus;

namespace TigerRAG.Infrastructure.Menus.Dal;

/// <summary>菜单配置 DAL：从 menu_config_record 表读取与写入菜单配置。业务规则校验由 Application 层负责。</summary>
public sealed class MenuConfigDal(TigerRagDbContext dbContext) : IMenuConfigDal
{
    public async Task<IReadOnlyList<MenuConfigItem>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await dbContext.MenuConfigs
            .OrderBy(config => config.ParentId ?? Guid.Empty)
            .ThenBy(config => config.SortOrder)
            .Select(config => new MenuConfigItem(
                config.Id,
                config.Key,
                config.Label,
                config.Icon,
                config.Roles ?? Array.Empty<string>(),
                config.ParentId,
                config.SortOrder,
                config.IsEnabled))
            .ToListAsync(cancellationToken);
    }

    public async Task<MenuConfigItem> CreateAsync(
        string key,
        string label,
        string? icon,
        IReadOnlyCollection<string> roles,
        Guid? parentId,
        int sortOrder,
        bool isEnabled,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var record = new menu_config_record
        {
            Id = Guid.NewGuid(),
            Key = key,
            Label = label,
            Icon = icon,
            Roles = roles?.ToArray() ?? Array.Empty<string>(),
            ParentId = parentId,
            SortOrder = sortOrder,
            IsEnabled = isEnabled,
        };
        dbContext.MenuConfigs.Add(record);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new MenuConfigItem(record.Id, record.Key, record.Label, record.Icon, record.Roles, record.ParentId, record.SortOrder, record.IsEnabled);
    }

    public async Task<MenuConfigItem?> UpdateAsync(
        Guid id,
        FieldUpdate<string> label,
        FieldUpdate<string> icon,
        FieldUpdate<IReadOnlyCollection<string>> roles,
        FieldUpdate<Guid?> parentId,
        FieldUpdate<int> sortOrder,
        FieldUpdate<bool> isEnabled,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var record = await dbContext.MenuConfigs.FindAsync(new object[] { id }, cancellationToken);
        if (record is null) return null;

        // FieldUpdate{T}.HasValue 区分"不修改"（Skip）与"设为指定值"（Set）
        if (label.HasValue) record.Label = label.Value!;
        if (icon.HasValue) record.Icon = icon.Value;
        if (roles.HasValue) record.Roles = roles.Value?.ToArray() ?? Array.Empty<string>();
        if (parentId.HasValue) record.ParentId = parentId.Value;
        if (sortOrder.HasValue) record.SortOrder = sortOrder.Value;
        if (isEnabled.HasValue) record.IsEnabled = isEnabled.Value;

        await dbContext.SaveChangesAsync(cancellationToken);
        return new MenuConfigItem(record.Id, record.Key, record.Label, record.Icon, record.Roles, record.ParentId, record.SortOrder, record.IsEnabled);
    }

    /// <summary>删除菜单及其全部后代（BFS 收集后按深度倒序删除，确保子行先于父行被移除）。返回删除的节点数；不存在时返回 0。</summary>
    public async Task<int> DeleteSubtreeAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var all = await dbContext.MenuConfigs
            .Select(r => new MenuNode(r.Id, r.ParentId))
            .ToListAsync(cancellationToken);

        if (!all.Any(n => n.Id == id))
            return 0;

        // BFS 收集 id 的全部后代
        var toDelete = new List<Guid> { id };
        var visited = new HashSet<Guid> { id };
        var queue = new Queue<Guid>();
        queue.Enqueue(id);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var child in all.Where(n => n.ParentId == current))
            {
                if (visited.Add(child.Id))
                {
                    toDelete.Add(child.Id);
                    queue.Enqueue(child.Id);
                }
            }
        }

        var records = await dbContext.MenuConfigs
            .Where(r => toDelete.Contains(r.Id))
            .ToListAsync(cancellationToken);

        // 按深度倒序删除：叶子先于根
        foreach (var record in records.OrderByDescending(r => ComputeDepth(r.Id, all)))
            dbContext.MenuConfigs.Remove(record);

        await dbContext.SaveChangesAsync(cancellationToken);
        return toDelete.Count;
    }

    private static int ComputeDepth(Guid id, List<MenuNode> all)
    {
        var depth = 0;
        var current = id;
        while (true)
        {
            var parent = all.FirstOrDefault(n => n.Id == current)?.ParentId;
            if (parent is null) break;
            depth++;
            current = parent.Value;
            if (depth > 1000) throw new InvalidOperationException("菜单层级过深或存在循环。");
        }
        return depth;
    }
}