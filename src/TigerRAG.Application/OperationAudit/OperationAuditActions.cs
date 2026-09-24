namespace TigerRAG.Application.OperationAudit;

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
    public const string KbBatchDelete = "kb.batchDelete";
    public const string KbReindex = "kb.reindex";
    public const string KbPermissionsReplace = "kb.permissions.replace";

    public const string DocumentCreate = "document.create";
    public const string DocumentDelete = "document.delete";
    public const string DocumentBatchDelete = "document.batchDelete";
    public const string DocumentReindex = "document.reindex";
    public const string DocumentIndexStart = "document.index.start";
    public const string DocumentIndexSuccess = "document.index.success";
    public const string DocumentIndexFailed = "document.index.failed";
}
