using System.ComponentModel.DataAnnotations;

namespace TigerRAG.Api.Controllers.Auth;

/// <summary>登录请求体：用户名 + 客户端 MD5(password+salt) 哈希。</summary>
public sealed record LoginRequest(
    [Required] string UserName,
    [Required] string PasswordHash);