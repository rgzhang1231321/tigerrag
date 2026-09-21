namespace TigerRAG.Application.Menus;

/// <summary>菜单配置的业务规则校验：父节点合法性、循环检测、可见角色名单。无状态纯函数，便于单元测试。</summary>
public static class MenuDomainService
{
    /// <summary>校验 parentId 是否存在、不是自身、且不构成祖先循环。</summary>
    /// <param name="id">当前菜单 Id。</param>
    /// <param name="parentId">待设置的父节点 Id；null 表示顶级菜单。</param>
    /// <param name="allItems">当前全部菜单项，用于查找后代链。</param>
    /// <exception cref="ArgumentException">业务规则违反。</exception>
    public static void ValidateParent(Guid id, Guid? parentId, IReadOnlyList<MenuConfigItem> allItems)
    {
        if (parentId is null) return;
        if (parentId == id) throw new ArgumentException("菜单不能以自身作为父节点。", nameof(parentId));

        if (!allItems.Any(item => item.Id == parentId))
            throw new ArgumentException($"父菜单 {parentId} 不存在。", nameof(parentId));

        // 检测 parentId 是否落在 id 的后代链中——若是，设置后会形成环。
        // 从 id 出发 BFS 向下遍历所有后代；若遇到 parentId 则构成循环。
        var queue = new Queue<Guid>();
        queue.Enqueue(id);
        var visited = new HashSet<Guid> { id };
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var child in allItems.Where(item => item.ParentId == current))
            {
                if (child.Id == parentId)
                    throw new ArgumentException("检测到祖先循环，无法设置该父节点。");
                if (visited.Add(child.Id))
                    queue.Enqueue(child.Id);
            }
        }
    }

    /// <summary>校验菜单可见角色名单：各项非空、无重复；null 表示清空为"所有人可见"。</summary>
    /// <param name="roles">待校验的角色名单；null 或空数组均表示所有人可见。</param>
    /// <exception cref="ArgumentException">角色名单含空项或重复项。</exception>
    public static void ValidateRoles(IReadOnlyCollection<string>? roles)
    {
        if (roles is null) return;
        foreach (var role in roles)
        {
            if (string.IsNullOrWhiteSpace(role))
                throw new ArgumentException("角色名不能为空。", nameof(roles));
        }
        if (roles.Distinct(StringComparer.Ordinal).Count() != roles.Count)
            throw new ArgumentException("菜单角色名单存在重复项。", nameof(roles));
    }
}