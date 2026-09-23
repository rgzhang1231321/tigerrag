namespace TigerRAG.Api.Controllers.Menus;

/// <summary>新建菜单配置请求。Roles 为空数组表示所有人可见。</summary>
public sealed record CreateMenuConfigRequest(
    string Key,
    string Label,
    string? Icon,
    string[] Roles,
    Guid? ParentId,
    int SortOrder,
    bool IsEnabled);