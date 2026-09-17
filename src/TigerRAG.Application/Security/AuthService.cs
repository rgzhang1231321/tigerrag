namespace TigerRAG.Application.Security;

/// <summary>
/// 认证领域服务。<c>passwordHash</c> 为客户端提交的 <c>MD5(password+salt)</c>；
/// 服务端再用 DB 中的 salt 拼接后由 Identity 的 PBKDF2 做存储层校验。
/// </summary>
public sealed class AuthService(
    IUserDal users,
    IUserCredentialDal credentials,
    IAccessTokenIssuer tokens,
    IRefreshSessionDal refreshSessions)
{
    /// <summary>取用户的 salt（用于客户端拼接 MD5）；用户不存在时返回 null，便于 Controller 区分 404。</summary>
    public Task<string?> GetSaltAsync(string userName, CancellationToken cancellationToken) =>
        users.GetPasswordSaltAsync(userName, cancellationToken);

    /// <summary>登录：凭据通过即同时签发 AccessToken 与 RefreshToken。</summary>
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
        return new LoginResult(user, tokens.Issue(user), refreshToken);
    }

    /// <summary>刷新：原会话原子撤销并发新会话。</summary>
    public async Task<LoginResult?> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var session = await refreshSessions.RotateAsync(refreshToken, cancellationToken);
        return session is null
            ? null
            : new LoginResult(session.User, tokens.Issue(session.User), session.RefreshToken);
    }

    /// <summary>登出：撤销当前刷新会话即可；AccessToken 由其短过期自然失效。</summary>
    public Task LogoutAsync(string refreshToken, CancellationToken cancellationToken) =>
        refreshSessions.RevokeAsync(refreshToken, cancellationToken);

    /// <summary>改密成功后必须撤销该用户全部刷新会话，防止旧设备继续访问。</summary>
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
        }

        return changed;
    }
}
