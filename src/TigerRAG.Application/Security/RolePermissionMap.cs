namespace TigerRAG.Application.Security;

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

    public static bool IsAllowed(string role, string permission) =>
        PermissionsByRole.TryGetValue(role, out var permissions) && permissions.Contains(permission);

    public static IReadOnlyCollection<string> RolesFor(string permission) => PermissionsByRole
        .Where(pair => pair.Value.Contains(permission))
        .Select(pair => pair.Key)
        .ToArray();

    private static IReadOnlySet<string> Set(params string[] values) =>
        new HashSet<string>(values, StringComparer.Ordinal);
}
