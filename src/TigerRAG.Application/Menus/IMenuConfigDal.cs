using TigerRAG.Application.Shared;

namespace TigerRAG.Application.Menus;

/// <summary>菜单配置 DAL 端口。</summary>
public interface IMenuConfigDal
{
    /// <summary>按 SortOrder 列出全部菜单配置。</summary>
    Task<IReadOnlyList<MenuConfigItem>> ListAsync(CancellationToken cancellationToken);

    /// <summary>新建菜单配置，返回落库后的完整视图。</summary>
    Task<MenuConfigItem> CreateAsync(
        string key,
        string label,
        string? icon,
        IReadOnlyCollection<string> roles,
        Guid? parentId,
        int sortOrder,
        bool isEnabled,
        CancellationToken cancellationToken);

    /// <summary>更新菜单配置；记录不存在时返回 null。每个字段用 <see cref="TigerRAG.Application.Shared.FieldUpdate{T}"/> 包装以区分"不修改"与"清空"。</summary>
    Task<MenuConfigItem?> UpdateAsync(
        Guid id,
        FieldUpdate<string> label,
        FieldUpdate<string> icon,
        FieldUpdate<IReadOnlyCollection<string>> roles,
        FieldUpdate<Guid?> parentId,
        FieldUpdate<int> sortOrder,
        FieldUpdate<bool> isEnabled,
        CancellationToken cancellationToken);

    /// <summary>删除菜单配置及其全部后代（业务级联）；记录不存在时返回 0 表示不存在。</summary>
    Task<int> DeleteSubtreeAsync(Guid id, CancellationToken cancellationToken);
}