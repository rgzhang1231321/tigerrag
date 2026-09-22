namespace TigerRAG.Infrastructure.Menus.Dal;

/// <summary>菜单子树 BFS 中间态：仅 Id 与 ParentId，避免拉整行数据。</summary>
internal sealed record MenuNode(Guid Id, Guid? ParentId);