namespace TigerRAG.Application.Menus;

/// <summary>菜单配置视图：前端导航渲染与管理页共用。角色列表为空表示所有人可见。</summary>
public sealed record MenuConfigItem(
    Guid Id,
    string Key,
    string Label,
    string? Icon,
    string[] Roles,
    Guid? ParentId,
    int SortOrder,
    bool IsEnabled);