namespace TigerRAG.Application.Security;

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
