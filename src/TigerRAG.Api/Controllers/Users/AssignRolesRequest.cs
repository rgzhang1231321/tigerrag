using System.ComponentModel.DataAnnotations;

namespace TigerRAG.Api.Controllers.Users;

/// <summary>分配角色请求体：替换该用户的全部角色。</summary>
public sealed record AssignRolesRequest([Required] IReadOnlyCollection<string> Roles);