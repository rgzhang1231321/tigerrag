namespace TigerRAG.Application.Auth;

/// <summary>矩阵视图：按菜单分组的 endpoint 列表 + 授权状态。</summary>
public sealed record RoleEndpointMatrix(IReadOnlyList<MenuGroup> Menus);