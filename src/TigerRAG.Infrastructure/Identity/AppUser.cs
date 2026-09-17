using Microsoft.AspNetCore.Identity;

namespace TigerRAG.Infrastructure.Identity;

/// <summary>
/// Identity 用户实体；以 Guid 作为主键。扩展字段：
/// <see cref="PasswordSalt"/> 用于客户端 MD5 拼接（salt 不由客户端提供，避免离线暴力破解）；
/// <see cref="CreatedAt"/> 用于审计/列表排序。
/// </summary>
public sealed class AppUser : IdentityUser<Guid>
{
    /// <summary>固定 64 字符十六进制 salt；用于 <c>MD5(password+salt)</c> 的服务端拼接与存储层 PBKDF2 校验。</summary>
    public string PasswordSalt { get; set; } = string.Empty;

    /// <summary>用户创建时间；列表按其升序展示。</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}