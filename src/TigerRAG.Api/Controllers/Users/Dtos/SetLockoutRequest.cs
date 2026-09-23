using System.ComponentModel.DataAnnotations;

namespace TigerRAG.Api.Controllers.Users;

/// <summary>锁口请求体：true=锁定，false=解锁。</summary>
public sealed record SetLockoutRequest([Required] bool Locked);