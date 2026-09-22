namespace TigerRAG.Application.Users;

/// <summary>JWT 撤权校验器需要的最小用户视图：stamp 与当前是否锁定。</summary>
public sealed record RevocationSnapshot(string SecurityStamp, bool IsLocked);