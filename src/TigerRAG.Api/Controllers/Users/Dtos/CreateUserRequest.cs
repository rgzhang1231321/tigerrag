using System.ComponentModel.DataAnnotations;

namespace TigerRAG.Api.Controllers.Users;

/// <summary>创建用户请求体：用户名与角色集合。</summary>
public sealed record CreateUserRequest(
    [Required] string UserName,
    [Required] IReadOnlyCollection<string> Roles);