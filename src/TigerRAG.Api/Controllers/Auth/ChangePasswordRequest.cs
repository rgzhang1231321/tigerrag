using System.ComponentModel.DataAnnotations;

namespace TigerRAG.Api.Controllers.Auth;

/// <summary>修改当前用户密码请求体：当前密码哈希 + 新密码哈希。</summary>
public sealed record ChangePasswordRequest(
    [Required] string CurrentPasswordHash,
    [Required] string NewPasswordHash);