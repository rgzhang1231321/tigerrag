namespace TigerRAG.Application.Users;

/// <summary>
/// 聚合根对外暴露的用户视图，含 Id、用户名、角色集合与 Identity 的 SecurityStamp。
/// SecurityStamp 由 <c>UserManager.UpdateSecurityStampAsync</c> 维护，是 JWT 撤权比对基准：
/// 任何敏感动作（角色、密码、锁口）轮换该值；签发器把它写入 JWT，校验器把 token 中的
/// claim 与缓存/DB 实时值比对，不一致即视为失效。
/// </summary>
public sealed record UserAccount(Guid Id, string UserName, IReadOnlyList<string> Roles)
{
    /// <summary>当前用户的 Identity SecurityStamp；创建时未填写则默认空串，由 <c>UserDal.MapAsync</c> 填实。</summary>
    public string SecurityStamp { get; init; } = string.Empty;
}