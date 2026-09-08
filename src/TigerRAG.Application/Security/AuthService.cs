namespace TigerRAG.Application.Security;

public sealed class AuthService(
    IUserDal users,
    IUserCredentialDal credentials,
    IAccessTokenIssuer tokens,
    IRefreshSessionDal refreshSessions)
{
    public async Task<LoginResult?> LoginAsync(
        string userName,
        string password,
        CancellationToken cancellationToken)
    {
        var user = await users.ValidateCredentialsAsync(userName, password, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var refreshToken = await refreshSessions.CreateAsync(user.Id, cancellationToken);
        return new LoginResult(user, tokens.Issue(user), refreshToken);
    }

    public async Task<LoginResult?> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var session = await refreshSessions.RotateAsync(refreshToken, cancellationToken);
        return session is null
            ? null
            : new LoginResult(session.User, tokens.Issue(session.User), session.RefreshToken);
    }

    public Task LogoutAsync(string refreshToken, CancellationToken cancellationToken) =>
        refreshSessions.RevokeAsync(refreshToken, cancellationToken);

    public async Task<bool> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken)
    {
        var changed = await credentials.ChangePasswordAsync(
            userId,
            currentPassword,
            newPassword,
            cancellationToken);
        if (changed)
        {
            await refreshSessions.RevokeAllAsync(userId, cancellationToken);
        }

        return changed;
    }
}
