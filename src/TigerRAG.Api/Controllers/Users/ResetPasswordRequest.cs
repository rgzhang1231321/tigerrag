using System.ComponentModel.DataAnnotations;

namespace TigerRAG.Api.Controllers.Users;

/// <summary>管理员重置密码请求体：客户端 MD5(password+salt) 哈希。</summary>
public sealed record ResetPasswordRequest([Required] string PasswordHash);