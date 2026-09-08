namespace TigerRAG.Infrastructure.Persistence.Entities;

public sealed class DocumentPermissionRecord
{
    public Guid DocumentId { get; set; }
    public PermissionPrincipalType PrincipalType { get; set; }
    public Guid PrincipalId { get; set; }
}

public enum PermissionPrincipalType
{
    User,
    Role
}
