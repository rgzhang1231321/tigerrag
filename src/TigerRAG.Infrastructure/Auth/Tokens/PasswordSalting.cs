using System.Security.Cryptography;
using System.Text;

namespace TigerRAG.Infrastructure.Auth;

/// <summary>
/// 密码 salt 生成 + MD5 拼接工具。MD5 仅作传输层哈希，存储仍走 Identity 的 PBKDF2 PasswordHasher。
/// </summary>
internal static class PasswordSalting
{
    /// <summary>生成 32 字节随机 salt，编码为 64 字符十六进制字符串。</summary>
    public static string GenerateSalt()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes);
    }

    /// <summary>计算 <c>MD5(password + salt)</c>，输出小写 32 字符十六进制字符串。</summary>
    public static string ComputeMd5Hash(string password, string salt)
    {
        var input = Encoding.UTF8.GetBytes(password + salt);
        var hash = MD5.HashData(input);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>把服务端 salt 与客户端提交的 <c>passwordHash</c> 拼接后交给 Identity PBKDF2 存储/校验。</summary>
    public static string Combine(string salt, string passwordHash) => $"{salt}:{passwordHash}";
}