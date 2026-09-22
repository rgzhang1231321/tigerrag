using System.ComponentModel.DataAnnotations;

namespace TigerRAG.Api.Controllers.Auth;

/// <summary>取盐请求体：仅需用户名。</summary>
public sealed record SaltRequest([Required] string UserName);