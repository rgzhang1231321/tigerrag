namespace TigerRAG.Application.Security;

/// <summary>
/// 密码哈希格式校验：客户端按 MD5(password+salt) 提交哈希，MD5 输出恒为 32 位小写 16 进制。
/// 服务端从未见过原始密码，只能以 MD5 输出格式作为最小约束。
/// </summary>
public static class PasswordHashFormat
{
    /// <summary>校验失败抛 <see cref="ArgumentException"/>；调用方转业务码 Validation。</summary>
    public static void EnsureAcceptable(string passwordHash)
    {
        if (string.IsNullOrEmpty(passwordHash) || passwordHash.Length != 32)
        {
            throw new ArgumentException("Password hash must be a 32-character lowercase hexadecimal MD5 string.", nameof(passwordHash));
        }

        for (var i = 0; i < passwordHash.Length; i++)
        {
            var c = passwordHash[i];
            var isLowerHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
            if (!isLowerHex)
            {
                throw new ArgumentException("Password hash must be a 32-character lowercase hexadecimal MD5 string.", nameof(passwordHash));
            }
        }
    }
}