namespace TigerRAG.Application.Security;

/// <summary>系统固定角色。权限通路为 用户 → 角色 → 菜单，菜单可见性由角色名单决定。</summary>
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
