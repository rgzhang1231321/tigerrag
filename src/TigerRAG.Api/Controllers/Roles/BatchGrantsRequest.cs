using TigerRAG.Application.Auth;

namespace TigerRAG.Api.Controllers.Roles;

/// <summary>批量应用角色授权请求：仅 granted=true 的项会落库；其它视为撤销目标。</summary>
public sealed record BatchGrantsRequest(IReadOnlyList<BatchEndpointChange> Endpoints);