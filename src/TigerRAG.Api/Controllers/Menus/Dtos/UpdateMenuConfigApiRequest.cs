namespace TigerRAG.Api.Controllers.Menus;

/// <summary>更新菜单配置请求：每个字段 null 表示"不修改"。</summary>
public sealed record UpdateMenuConfigApiRequest(
    string? Label,
    string? Icon,
    string[]? Roles,
    Guid? ParentId,
    int? SortOrder,
    bool? IsEnabled);