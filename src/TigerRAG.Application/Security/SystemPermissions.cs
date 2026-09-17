namespace TigerRAG.Application.Security;

/// <summary>权限代码常量与集合；Controller 通过 Policy 名引用，RolePermissionMap 完成角色 → 权限的展开。</summary>
public static class SystemPermissions
{
    public const string ManageUsers = "users.manage";
    public const string ManageKnowledgeBases = "knowledge-bases.manage";
    public const string ManageDocuments = "documents.manage";
    public const string UseChat = "chat.use";
    public const string ReadAudit = "audit.read";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(
        [ManageUsers, ManageKnowledgeBases, ManageDocuments, UseChat, ReadAudit],
        StringComparer.Ordinal);
}
