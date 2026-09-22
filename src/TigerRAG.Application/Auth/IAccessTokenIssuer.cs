using TigerRAG.Application.Users;

namespace TigerRAG.Application.Auth;

/// <summary>JWT 签发端口；Application 不感知 JwtSecurityToken 等具体实现。</summary>
public interface IAccessTokenIssuer
{
    /// <summary>按用户角色签发 JWT；role claim 由 UserAccount.Roles 直接写入。</summary>
    Task<AccessToken> IssueAsync(UserAccount user, CancellationToken cancellationToken);
}