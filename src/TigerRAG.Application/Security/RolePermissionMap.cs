namespace TigerRAG.Application.Security;

/// <summary>角色 → 权限的固定映射。一期不做动态权限编辑器，新增角色/权限需要改本类并发布。</summary>
public static class RolePermissionMap
{
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> PermissionsByRole =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            [SystemRoles.Admin] = SystemPermissions.All,
            [SystemRoles.KbManager] = Set(
                SystemPermissions.ManageKnowledgeBases,
                SystemPermissions.ManageDocuments,
                SystemPermissions.UseChat),
            [SystemRoles.Editor] = Set(
                SystemPermissions.ManageDocuments,
                SystemPermissions.UseChat),
            [SystemRoles.Viewer] = Set(SystemPermissions.UseChat),
            [SystemRoles.Auditor] = Set(SystemPermissions.ReadAudit)
        };

    /// <summary>判断单个角色是否拥有某项权限。</summary>
    public static bool IsAllowed(string role, string permission) =>
        PermissionsByRole.TryGetValue(role, out var permissions) && permissions.Contains(permission);

    /// <summary>列出拥有某项权限的全部角色；用于 JWT 策略注册。</summary>
    public static IReadOnlyCollection<string> RolesFor(string permission) => PermissionsByRole
        .Where(pair => pair.Value.Contains(permission))
        .Select(pair => pair.Key)
        .ToArray();

    /// <summary>
    /// 按角色集合聚合出全部权限，用于写入 JWT 的 permission claim。
    /// 服务端是权限映射的唯一来源；前端不得自行从 role 推导 permission。
    /// </summary>
    public static IReadOnlySet<string> PermissionsFor(IEnumerable<string> roles)
    {
        var merged = new HashSet<string>(StringComparer.Ordinal);
        foreach (var role in roles)
        {
            if (PermissionsByRole.TryGetValue(role, out var permissions))
            {
                foreach (var permission in permissions)
                {
                    merged.Add(permission);
                }
            }
        }
        return merged;
    }

    private static IReadOnlySet<string> Set(params string[] values) =>
        new HashSet<string>(values, StringComparer.Ordinal);
}
