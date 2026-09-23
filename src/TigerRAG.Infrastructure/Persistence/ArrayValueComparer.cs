namespace TigerRAG.Infrastructure.Persistence;

/// <summary>
/// EF Core 值比较器：逐元素比较数组内容，用于 string[] 等集合属性搭配 HasConversion 时使用。
/// 变更追踪时 EF Core 借此判断数组内容是否真正变化，避免每次都触发不必要的 UPDATE。
/// </summary>
/// <typeparam name="T">数组元素类型。</typeparam>
public sealed class ArrayValueComparer<T> : Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<T[]>
{
    /// <summary>逐元素顺序比较两个数组是否相等；null 视为空数组。</summary>
    public ArrayValueComparer()
        : base(
            (c1, c2) =>
                (c1 == null && c2 == null) ||
                (c1 != null && c2 != null && c1.SequenceEqual(c2)),
            c => c == null
                ? 0
                : c.Aggregate(0, (hash, item) =>
                    System.HashCode.Combine(hash, item == null ? 0 : item.GetHashCode())),
            c => c == null ? null : c.ToArray())
    {
    }
}
