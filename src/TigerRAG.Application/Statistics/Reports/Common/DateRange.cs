namespace TigerRAG.Application.Statistics.Reports;

/// <summary>日期范围。</summary>
public sealed record DateRange(DateTimeOffset Start, DateTimeOffset End)
{
    /// <summary>天数。</summary>
    public int Days => (int)(End - Start).TotalDays + 1;
}