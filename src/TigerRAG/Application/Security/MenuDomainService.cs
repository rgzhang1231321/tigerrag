namespace TigerRAG.Application.Security;

/// <summary>菜单配置的业务规则校验：父节点合法性、循环检测、权限码归属。无状态纯函数，便于单元测试。</summary>
public static class MenuDomainService
{
    /// <summary>校验 parentId 是否存在、不是自身、且不构成祖先循环。</summary>
    /// <param name="id">当前菜单 Id。</param>
    /// <param name="parentId">待设置的父节点 Id；null 表示顶级菜单。</param>
    /// <param name="allItems">当前全部菜单项，用于查找祖先链。</param>
    /// <exception cref="ArgumentException">业务规则违反。</exception>
    public static void ValidateParent(Guid id, Guid? parentId, IReadOnlyList<MenuConfigItem> allItems)
    {
        if (parentId is null) return;
        if (parentId == id) throw new ArgumentException("菜单不能以自身作为父节点。", nameof(parentId));

        var parent = allItems.FirstOrDefault(item => item.Id == parentId);
        if (parent is null)
            throw new ArgumentException($"父菜单 {parentId} 不存在。", nameof(parentId));

        // 沿 parent 链向上追溯，若回到 id 自身则构成循环。
        var visited = new HashSet<Guid> { id };
        var current = parent;
        while (current.ParentId is not null)
        {
            if (!visited.Add(current.ParentId.Value))
                throw new ArgumentException("检测到祖先循环，无法设置该父节点。");
            current = allItems.FirstOrDefault(item => item.Id == current.ParentId.Value)
                ?? throw new ArgumentException($"父链中存在未知节点 {current.ParentId}。");
        }
    }

    /// <summary>校验权限代码是否属于 <see cref="SystemPermissions.All"/>；null 表示不绑定权限（允许）。</summary>
    /// <exception cref="ArgumentException">权限码未知。</exception>
    public static void ValidatePermission(string? permission)
    {
        if (permission is null) return;
        if (!SystemPermissions.All.Contains(permission))
            throw new ArgumentException($"未知的权限代码：{permission}", nameof(permission));
    }
}
