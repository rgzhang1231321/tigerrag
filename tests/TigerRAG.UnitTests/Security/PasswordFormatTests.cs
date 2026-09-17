using TigerRAG.Application.Security;

namespace TigerRAG.UnitTests.Security;

/// <summary>
/// 验证客户端提交的 MD5 输出格式约束：MD5 输出恒为 32 位小写 16 进制，
/// 任何其它形式（短串、大写、非 hex、空）都视为非合规，服务端必须拒绝。
/// </summary>
public sealed class PasswordFormatTests
{
    [Fact]
    public void EnsureAcceptable_WithValidLowerHex32_DoesNotThrow()
    {
        var hash = new string('a', 32);

        PasswordHashFormat.EnsureAcceptable(hash);
    }

    [Fact]
    public void EnsureAcceptable_WithUpperCase_Throws()
    {
        var hash = new string('A', 32);

        Assert.Throws<ArgumentException>(() => PasswordHashFormat.EnsureAcceptable(hash));
    }

    [Fact]
    public void EnsureAcceptable_WithWrongLength_Throws()
    {
        Assert.Throws<ArgumentException>(() => PasswordHashFormat.EnsureAcceptable("abc"));
        Assert.Throws<ArgumentException>(() => PasswordHashFormat.EnsureAcceptable(new string('a', 33)));
    }

    [Fact]
    public void EnsureAcceptable_WithNonHexChar_Throws()
    {
        var hash = "g" + new string('a', 31);

        Assert.Throws<ArgumentException>(() => PasswordHashFormat.EnsureAcceptable(hash));
    }

    [Fact]
    public void EnsureAcceptable_WithEmpty_Throws()
    {
        Assert.Throws<ArgumentException>(() => PasswordHashFormat.EnsureAcceptable(string.Empty));
    }
}