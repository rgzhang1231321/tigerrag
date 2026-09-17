namespace TigerRAG.Infrastructure.Persistence.Entities;

/// <summary>导航菜单配置记录。前端从本表动态读取并渲染导航。</summary>
public sealed class menu_config_record
{
    public Guid Id { get; set; }
    public required string Key { get; set; }
    public required string Label { get; set; }
    public string? Icon { get; set; }
    public string? Permission { get; set; }
    public Guid? ParentId { get; set; }
    public int SortOrder { get; set; }
    public bool IsEnabled { get; set; }
}
