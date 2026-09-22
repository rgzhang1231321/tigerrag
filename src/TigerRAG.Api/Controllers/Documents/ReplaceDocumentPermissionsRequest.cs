using System.ComponentModel.DataAnnotations;

namespace TigerRAG.Api.Controllers.Documents;

/// <summary>替换文档权限请求体：允许访问文档的用户标识与角色集合。</summary>
public sealed record ReplaceDocumentPermissionsRequest(
    [Required] IReadOnlyCollection<Guid> UserIds,
    [Required] IReadOnlyCollection<string> Roles);