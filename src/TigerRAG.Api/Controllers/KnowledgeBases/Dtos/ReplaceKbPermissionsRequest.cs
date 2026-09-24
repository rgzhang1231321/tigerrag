using System.ComponentModel.DataAnnotations;

namespace TigerRAG.Api.Controllers.KnowledgeBases;

/// <summary>替换知识库权限请求体：允许访问知识库的用户标识与角色集合。</summary>
public sealed record ReplaceKbPermissionsRequest(
    [Required] IReadOnlyCollection<Guid> UserIds,
    [Required] IReadOnlyCollection<string> Roles);
