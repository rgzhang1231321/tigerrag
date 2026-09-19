namespace TigerRAG.Application.Security;

/// <summary>表示一个可选更新字段：<c>HasValue=false</c> 表示不修改；<c>HasValue=true</c> 且 <c>Value=null</c> 表示清空。
/// 解决 JSON 反序列化无法区分"不传"与"传 null"的问题。</summary>
public readonly record struct FieldUpdate<T>(bool HasValue, T? Value)
{
    /// <summary>不修改该字段。</summary>
    public static FieldUpdate<T> Skip() => default;

    /// <summary>将该字段设为 <paramref name="value"/>（null 表示清空）。</summary>
    public static FieldUpdate<T> Set(T? value) => new(true, value);
}
