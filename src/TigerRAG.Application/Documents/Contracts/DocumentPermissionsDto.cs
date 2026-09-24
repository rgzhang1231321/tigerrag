namespace TigerRAG.Application.Documents;

/// <summary>文档当前 ACL 状态：已授权用户 Id 与角色名集合。由 Service 层返回，Controller 直接透传。</summary>
public sealed record DocumentPermissionsDto(
    IReadOnlyCollection<Guid> UserIds,
    IReadOnlyCollection<string> Roles);
