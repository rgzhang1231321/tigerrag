using TigerRAG.Infrastructure.Logging;

namespace TigerRAG.UnitTests.Logging.Masking;

/// <summary>
/// 验证 <see cref="SensitiveJsonMasker"/>：密文字段（密码哈希、token 等）必须在落库前被替换，
/// 非敏感数据保持原样；解析失败（含截断的坏 JSON）时正则兜底，绝不放行未脱敏密文。
/// </summary>
public sealed class SensitiveJsonMaskerTests
{
    [Fact]
    public void Mask_PasswordHashAtRoot_ReplacesValueWithMask()
    {
        var input = """{"userName":"admin","passwordHash":"secret-hash"}""";

        var masked = SensitiveJsonMasker.Mask(input);

        // 密文必须消失，占位符必须出现，非敏感值保持原样。
        Assert.DoesNotContain("secret-hash", masked);
        Assert.Contains("***", masked);
        Assert.Contains("admin", masked);
    }

    [Fact]
    public void Mask_NestedSensitiveFields_RecursesIntoObjectsAndArrays()
    {
        var input = """
            {
              "userName": "admin",
              "credentials": [
                { "currentPasswordHash": "old-secret", "newPasswordHash": "new-secret" },
                { "note": "keep" }
              ],
              "clientSecret": "top-secret",
              "authorization": "Bearer abc"
            }
            """;

        var masked = SensitiveJsonMasker.Mask(input);

        Assert.DoesNotContain("old-secret", masked);
        Assert.DoesNotContain("new-secret", masked);
        Assert.DoesNotContain("top-secret", masked);
        Assert.DoesNotContain("Bearer abc", masked);
        // 非敏感值保持原样。
        Assert.Contains("admin", masked);
        Assert.Contains("keep", masked);
    }

    [Fact]
    public void Mask_TokenKeys_MaskedCaseInsensitively()
    {
        var input = """{"RefreshToken":"abc","apiToken":"def"}""";

        var masked = SensitiveJsonMasker.Mask(input);

        Assert.DoesNotContain("abc", masked);
        Assert.DoesNotContain("def", masked);
    }

    [Fact]
    public void Mask_InvalidJson_AppliesRegexFallbackAndStillMasksSecrets()
    {
        // 截断的坏 JSON（读取超上限时出现）：解析失败，但已完整的密文字段绝不能原样落库。
        var input = """{"userName":"admin","passwordHash":"secret-hash","data":[1,2,""";

        var masked = SensitiveJsonMasker.Mask(input);

        Assert.DoesNotContain("secret-hash", masked);
        Assert.Contains("admin", masked);
    }

    [Fact]
    public void Mask_NonSensitiveJson_PreservesStructureAndValues()
    {
        var input = """{"page":1,"pageSize":20,"keyword":"kb","nested":{"flag":true,"items":[1,2,3]}}""";

        var masked = SensitiveJsonMasker.Mask(input);

        // 无敏感 key 时输出应与输入语义一致（紧凑序列化）。
        Assert.Contains("\"page\":1", masked);
        Assert.Contains("\"keyword\":\"kb\"", masked);
        Assert.Contains("\"flag\":true", masked);
        Assert.Contains("[1,2,3]", masked);
    }

    [Fact]
    public void Mask_EmptyOrNullInput_ReturnsInputUnchanged()
    {
        // 中间件只在有内容时调用，但空输入必须安全直通而不是抛异常。
        Assert.Equal(string.Empty, SensitiveJsonMasker.Mask(string.Empty));
        Assert.Null(SensitiveJsonMasker.Mask(null!));
    }

    [Fact]
    public void Mask_NonAsciiValues_PreservedWithoutUnicodeEscaping()
    {
        var input = """{"userName":"admin","displayName":"张三","note":"知识库"}""";

        var masked = SensitiveJsonMasker.Mask(input);

        // 日志要给人看：默认编码器会把中文转义成 \uXXXX，必须放宽为可读输出。
        Assert.Contains("张三", masked);
        Assert.Contains("知识库", masked);
        Assert.Contains("admin", masked);
    }
}
