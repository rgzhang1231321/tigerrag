namespace TigerRAG.Application.Security;

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
