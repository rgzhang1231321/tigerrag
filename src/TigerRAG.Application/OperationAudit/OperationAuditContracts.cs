using TigerRAG.Application.Shared;

namespace TigerRAG.Application.OperationAudit;

/// <summary>审计列表单项。</summary>
public sealed record OperationAuditEntryDto(
    long Id,
    DateTimeOffset CreatedAt,
    Guid ActorId,
    string ActorName,
    string Action,
    string TargetType,
    string TargetId,
    string Summary);

/// <summary>审计列表查询条件。</summary>
public sealed record OperationAuditQueryRequest(
    DateTimeOffset? From,
    DateTimeOffset? To,
    Guid? ActorId,
    string? Action,
    string? Keyword,
    int Page,
    int PageSize);

/// <summary>审计列表查询结果。</summary>
public sealed record OperationAuditQueryResult(
    IReadOnlyList<OperationAuditEntryDto> Entries,
    int Total);

/// <summary>审计写入端口（业务埋点调用）。</summary>
public interface IOperationAuditWriter
{
    Task RecordAsync(OperationAuditEntry entry, CancellationToken cancellationToken);
}

/// <summary>审计查询端口（Controller 调用）。</summary>
public interface IOperationAuditDal
{
    Task<OperationAuditQueryResult> QueryAsync(
        ActorContext actor,
        OperationAuditQueryRequest request,
        CancellationToken cancellationToken);
}

/// <summary>一条审计记录。</summary>
public sealed record OperationAuditEntry(
    Guid ActorId,
    string ActorName,
    string Action,
    string TargetType,
    string TargetId,
    string Summary);

/// <summary>操作类型常量。</summary>
public static class OperationAuditActions
{
    public const string UserRolesAssign = "user.roles.assign";
    public const string UserCreate = "user.create";
    public const string UserInitialPassword = "user.password.initial";
    public const string UserPasswordReset = "user.password.reset";
    public const string UserDelete = "user.delete";
    public const string UserLockout = "user.lockout";
    public const string RoleCreate = "role.create";
    public const string RoleDelete = "role.delete";
    public const string RoleRename = "role.rename";
    public const string MenuCreate = "menu.create";
    public const string MenuUpdate = "menu.update";
    public const string MenuDelete = "menu.delete";
    public const string MenuReferencesUpdate = "menu.references.update";
    public const string DocumentPermissions = "document.permissions.replace";
    public const string AuthLogin = "auth.login";
    public const string AuthLogout = "auth.logout";
    public const string AuthPasswordChange = "auth.password.change";
    public const string RoleEndpointGrantAll = "role.endpoint.grant.all";
    public const string RoleEndpointRevokeAll = "role.endpoint.revoke.all";
    public const string RoleEndpointToggle = "role.endpoint.toggle";
    public const string RoleEndpointApplyBatch = "role.endpoint.applyBatch";

    public const string KbCreate = "kb.create";
    public const string KbUpdate = "kb.update";
    public const string KbDelete = "kb.delete";
    public const string KbReindex = "kb.reindex";

    public const string DocumentCreate = "document.create";
    public const string DocumentDelete = "document.delete";
    public const string DocumentReindex = "document.reindex";
    public const string DocumentIndexStart = "document.index.start";
    public const string DocumentIndexSuccess = "document.index.success";
    public const string DocumentIndexFailed = "document.index.failed";
}
