using Microsoft.EntityFrameworkCore;
using TigerRAG.Application.Security;
using TigerRAG.Infrastructure.Persistence;
using TigerRAG.Infrastructure.Persistence.Entities;

namespace TigerRAG.Infrastructure.Dal;

/// <summary>菜单配置 DAL：从 menu_config_record 表读取与写入菜单配置。</summary>
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
                config.Permission,
                config.ParentId,
                config.SortOrder,
                config.IsEnabled))
            .ToListAsync(cancellationToken);
    }

    public async Task<MenuConfigItem> CreateAsync(
        string key,
        string label,
        string? icon,
        string? permission,
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
            Permission = permission,
            ParentId = parentId,
            SortOrder = sortOrder,
            IsEnabled = isEnabled,
        };
        dbContext.MenuConfigs.Add(record);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new MenuConfigItem(record.Id, record.Key, record.Label, record.Icon, record.Permission, record.ParentId, record.SortOrder, record.IsEnabled);
    }

    public async Task<MenuConfigItem?> UpdateAsync(
        Guid id,
        string? label,
        string? icon,
        string? permission,
        Guid? parentId,
        int? sortOrder,
        bool? isEnabled,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var record = await dbContext.MenuConfigs.FindAsync(new object[] { id }, cancellationToken);
        if (record is null)
        {
            return null;
        }
        if (label is not null) record.Label = label;
        if (icon is not null) record.Icon = icon;
        if (permission is not null) record.Permission = permission;
        if (parentId is not null) record.ParentId = parentId;
        if (sortOrder is not null) record.SortOrder = sortOrder.Value;
        if (isEnabled is not null) record.IsEnabled = isEnabled.Value;
        await dbContext.SaveChangesAsync(cancellationToken);
        return new MenuConfigItem(record.Id, record.Key, record.Label, record.Icon, record.Permission, record.ParentId, record.SortOrder, record.IsEnabled);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var record = await dbContext.MenuConfigs.FindAsync(new object[] { id }, cancellationToken);
        if (record is null)
        {
            return false;
        }
        dbContext.MenuConfigs.Remove(record);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
