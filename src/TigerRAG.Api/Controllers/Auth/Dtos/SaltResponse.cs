namespace TigerRAG.Api.Controllers.Auth;

/// <summary>取盐响应体：返回 DB 中该用户的 salt。</summary>
public sealed record SaltResponse(string Salt);