using TigerRAG.Application.OperationAudit;
using TigerRAG.Application.Shared;
using TigerRAG.Application.Users;

namespace TigerRAG.Application.Auth;

/// <summary>
/// 认证领域服务。<c>passwordHash</c> 为客户端提交的 <c>MD5(password+salt)</c>；
/// 服务端再用 DB 中的 salt 拼接后由 Identity 的 PBKDF2 做存储层校验。
/// </summary>
public sealed class AuthService(
    IUserDal users,
    IUserCredentialDal credentials,
    IAccessTokenIssuer tokens,
    IRefreshSessionDal refreshSessions,
    IAuthRevocationCache revocationCache,
    IOperationAuditWriter auditWriter)
{
    /// <summary>取用户的 salt（用于客户端拼接 MD5）；用户不存在时返回 null，便于 Controller 区分 404。</summary>
    public Task<string?> GetSaltAsync(string userName, CancellationToken cancellationToken) =>
        users.GetPasswordSaltAsync(userName, cancellationToken);

    /// <summary>登录：凭据通过即同时签发 AccessToken 与 RefreshToken，并预热撤权缓存。</summary>
    public async Task<LoginResult?> LoginAsync(
        string userName,
        string passwordHash,
        CancellationToken cancellationToken)
    {
        var user = await users.ValidateCredentialsAsync(userName, passwordHash, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var refreshToken = await refreshSessions.CreateAsync(user.Id, cancellationToken);
        // 登录时预热 stamp 缓存，让后续 JWT 校验直接命中缓存，无需回退到 DB。
        // 缓存写失败不能阻塞登录——stamp 比较路径会回退到 DB 正确判 stamp。
        try
        {
            await revocationCache.SetStampAsync(user.Id, user.SecurityStamp, cancellationToken);
        }
        catch
        {
            // 缓存不可用时降级：后续请求走 DB 查 stamp。
        }

        return new LoginResult(
            user,
            await tokens.IssueAsync(user, cancellationToken),
            refreshToken);
    }

    /// <summary>刷新：原会话原子撤销并发新会话。</summary>
    public async Task<LoginResult?> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var session = await refreshSessions.RotateAsync(refreshToken, cancellationToken);
        return session is null
            ? null
            : new LoginResult(
                session.User,
                await tokens.IssueAsync(session.User, cancellationToken),
                session.RefreshToken);
    }

    /// <summary>登出：先拿用户身份（用于审计），再撤会话；AccessToken 由其短过期自然失效。</summary>
    public async Task LogoutAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var user = await refreshSessions.GetUserByTokenAsync(refreshToken, cancellationToken);
        await refreshSessions.RevokeAsync(refreshToken, cancellationToken);

        // 审计：用户可能已不存在（如被管理员删除后 token 仍有效），此时跳过审计。
        if (user is not null)
        {
            await auditWriter.RecordAsync(new OperationAuditEntry(
                user.Id,
                user.UserName,
                OperationAuditActions.AuthLogout,
                "session",
                refreshToken.Length > 8 ? refreshToken[..8] : refreshToken,
                $"{user.UserName} 退出登录"), cancellationToken);
        }
    }

    /// <summary>
    /// 改密成功后撤销该用户全部刷新会话并清缓存。
    /// Identity 的 <c>ChangePasswordAsync</c> 已自动轮换 stamp，缓存中的旧 stamp 必须清掉，
    // 否则 TTL 内旧 JWT 仍可能命中旧 stamp 通过校验。
    /// </summary>
    public async Task<bool> ChangePasswordAsync(
        Guid userId,
        string currentPasswordHash,
        string newPasswordHash,
        CancellationToken cancellationToken)
    {
        var changed = await credentials.ChangePasswordAsync(
            userId,
            currentPasswordHash,
            newPasswordHash,
            cancellationToken);
        if (changed)
        {
            await refreshSessions.RevokeAllAsync(userId, cancellationToken);
            // 改密后清缓存：Identity 已自动轮换 stamp，下次请求会从 DB 重新拉，避免 TTL 内继续命中旧 stamp。
            await revocationCache.InvalidateAsync(userId, cancellationToken);
        }

        return changed;
    }
}
