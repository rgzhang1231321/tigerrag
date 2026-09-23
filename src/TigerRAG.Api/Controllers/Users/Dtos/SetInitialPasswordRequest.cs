using System.ComponentModel.DataAnnotations;

namespace TigerRAG.Api.Controllers.Users;

/// <summary>设置初始密码请求体：客户端 MD5(password+salt) 哈希。</summary>
public sealed record SetInitialPasswordRequest([Required] string PasswordHash);