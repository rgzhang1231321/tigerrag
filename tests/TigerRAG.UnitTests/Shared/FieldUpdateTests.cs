using TigerRAG.Application.Shared;

namespace TigerRAG.UnitTests.Shared;

/// <summary>验证 FieldUpdate 能区分"不修改"与"设为 null"——这是 UpdateMenuConfigRequest 的核心契约。</summary>
public sealed class FieldUpdateTests
    {
    [Fact]
    public static void Skip_HasValueIsFalse()
    {
        var field = FieldUpdate<string>.Skip();
        Assert.False(field.HasValue);
        Assert.Null(field.Value);
    }

    [Fact]
    public static void SetNull_HasValueIsTrue_ValueIsNull()
    {
        var field = FieldUpdate<string>.Set(null);
        Assert.True(field.HasValue);
        Assert.Null(field.Value);
    }

    [Fact]
    public static void SetValue_HasValueIsTrue_ValueIsPreserved()
    {
        var field = FieldUpdate<string>.Set("hello");
        Assert.True(field.HasValue);
        Assert.Equal("hello", field.Value);
    }

    [Fact]
    public static void Default_IsSkip()
    {
        FieldUpdate<string> field = default;
        Assert.False(field.HasValue);
    }

    [Fact]
    public static void Guid_SetNull_ClearsParent()
    {
        var field = FieldUpdate<Guid?>.Set(null);
        Assert.True(field.HasValue);
        Assert.Null(field.Value);
    }
}
