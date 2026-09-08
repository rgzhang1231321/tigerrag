namespace TigerRAG.Application.Security;

public sealed class UserRoleService(
    IUserDal users,
    IUserCredentialDal credentials,
    IRefreshSessionDal refreshSessions)
{
    public Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken cancellationToken) =>
        users.ListAsync(cancellationToken);

    public async Task AssignRolesAsync(
        Guid userId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken)
    {
        await users.AssignRolesAsync(userId, NormalizeRoles(roles), cancellationToken);
    }

    public Task<UserAccount> CreateAsync(
        string userName,
        string password,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken) =>
        credentials.CreateAsync(userName, password, NormalizeRoles(roles), cancellationToken);

    public async Task ResetPasswordAsync(
        Guid userId,
        string newPassword,
        CancellationToken cancellationToken)
    {
        await credentials.ResetPasswordAsync(userId, newPassword, cancellationToken);
        await refreshSessions.RevokeAllAsync(userId, cancellationToken);
    }

    private static string[] NormalizeRoles(IReadOnlyCollection<string> roles)
    {
        var invalidRole = roles.FirstOrDefault(role => !SystemRoles.All.Contains(role));
        if (invalidRole is not null)
        {
            throw new ArgumentException($"Unknown system role: {invalidRole}", nameof(roles));
        }

        return roles
            .Distinct(StringComparer.Ordinal)
            .OrderBy(role => role, StringComparer.Ordinal)
            .ToArray();
    }
}
