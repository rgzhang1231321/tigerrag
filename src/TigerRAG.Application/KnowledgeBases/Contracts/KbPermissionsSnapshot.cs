namespace TigerRAG.Application.KnowledgeBases;

/// <summary>KB ACL 快照：用户 Id 集合与角色 Id 集合。由 DAL 查询返回，Service 层负责角色 Id→Name 映射。</summary>
public sealed record KbPermissionsSnapshot(
    IReadOnlyCollection<Guid> UserIds,
    IReadOnlyCollection<Guid> RoleIds);
