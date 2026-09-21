using System;

namespace TigerRAG.Infrastructure.Persistence.Entities.Menus;

/// <summary>导航菜单配置记录。前端从本表动态读取并渲染导航。</summary>
public sealed class menu_config_record
{
    /// <summary>菜单主键。</summary>
    public Guid Id { get; set; }

    /// <summary>菜单唯一标识，前端路由 key 与 ACL 校验都依赖此字段。</summary>
    public required string Key { get; set; }

    /// <summary>菜单显示名称。</summary>
    public required string Label { get; set; }

    /// <summary>菜单图标名（前端图标库映射键）。</summary>
    public string? Icon { get; set; }

    /// <summary>可见角色名列表（逗号分隔）；空数组表示所有人可见，Admin 始终 bypass。</summary>
    public string[] Roles { get; set; } = Array.Empty<string>();

    /// <summary>上级菜单 Id；null 表示根菜单。</summary>
    public Guid? ParentId { get; set; }

    /// <summary>同级菜单的排序序号，升序展示。</summary>
    public int SortOrder { get; set; }

    /// <summary>是否启用；false 时前端隐藏该菜单。</summary>
    public bool IsEnabled { get; set; }
}
