namespace TigerRAG.Application.Roles;

/// <summary>角色视图：含用户引用数与关联菜单名称列表；所有角色平等，无系统/自定义区分。</summary>
public sealed record RoleDto(
    string Name,
    int UserCount,
    int MenuCount,
    IReadOnlyList<string> MenuNames);