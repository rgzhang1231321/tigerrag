namespace TigerRAG.Application.Statistics.Reports;

/// <summary>报表类型。</summary>
public enum ReportType
{
    /// <summary>文档统计。</summary>
    Documents = 1,

    /// <summary>用户活跃。</summary>
    Users = 2,

    /// <summary>对话分析。</summary>
    Conversations = 3,

    /// <summary>系统健康。</summary>
    System = 4,
}