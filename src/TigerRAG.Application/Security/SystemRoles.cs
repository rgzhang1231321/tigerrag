namespace TigerRAG.Application.Security;

/// <summary>系统固定角色。角色与权限的映射见 <see cref="RolePermissionMap"/>。</summary>
public static class SystemRoles
{
    public const string Admin = "Admin";
    public const string KbManager = "KbManager";
    public const string Editor = "Editor";
    public const string Viewer = "Viewer";
    public const string Auditor = "Auditor";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(
        [Admin, KbManager, Editor, Viewer, Auditor],
        StringComparer.Ordinal);
}
