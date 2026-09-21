namespace TigerRAG.Infrastructure.Persistence.Entities.OperationAudit;

/// <summary>安全操作审计持久化实体。无 FK 约束。</summary>
public sealed class operation_audit_record
{
    /// <summary>审计记录主键（bigserial）。</summary>
    public long Id { get; set; }

    /// <summary>操作者用户 Id。</summary>
    public Guid ActorId { get; set; }

    /// <summary>操作者登录名，冗余存储以便审计页面直接展示。</summary>
    public string ActorName { get; set; } = string.Empty;

    /// <summary>操作动作（如 CreateUser/UpdateRole/AssignDocumentAcl）。</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>操作对象类型（如 User/Role/Document）。</summary>
    public string TargetType { get; set; } = string.Empty;

    /// <summary>操作对象 Id，使用字符串以兼容非 Guid 主键资源。</summary>
    public string TargetId { get; set; } = string.Empty;

    /// <summary>人可读的操作摘要。</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>操作时间。</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
