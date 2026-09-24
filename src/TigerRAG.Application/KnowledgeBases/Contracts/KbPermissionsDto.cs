namespace TigerRAG.Application.KnowledgeBases;

/// <summary>KB 当前 ACL 状态：已授权用户 Id 与角色名集合。由 Service 层返回，Controller 直接透传。</summary>
public sealed record KbPermissionsDto(
    IReadOnlyCollection<Guid> UserIds,
    IReadOnlyCollection<string> Roles);
