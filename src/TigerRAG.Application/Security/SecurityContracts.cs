namespace TigerRAG.Application.Security;

public sealed record UserAccount(Guid Id, string UserName, IReadOnlyList<string> Roles);

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

public sealed record RefreshToken(string Value, DateTimeOffset ExpiresAt);

public sealed record RefreshSession(UserAccount User, RefreshToken RefreshToken);

public sealed record LoginResult(UserAccount User, AccessToken AccessToken, RefreshToken RefreshToken);

public sealed record DocumentAccessScope(bool AllDocuments, IReadOnlyList<Guid> DocumentIds);

public interface IUserDal
{
    Task<UserAccount?> ValidateCredentialsAsync(
        string userName,
        string password,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken cancellationToken);

    Task AssignRolesAsync(
        Guid userId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken);
}

public interface IAccessTokenIssuer
{
    AccessToken Issue(UserAccount user);
}

public interface IUserCredentialDal
{
    Task<UserAccount> CreateAsync(
        string userName,
        string password,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken);

    Task<bool> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken);

    Task ResetPasswordAsync(
        Guid userId,
        string newPassword,
        CancellationToken cancellationToken);
}

public interface IRefreshSessionDal
{
    Task<RefreshToken> CreateAsync(Guid userId, CancellationToken cancellationToken);

    Task<RefreshSession?> RotateAsync(string value, CancellationToken cancellationToken);

    Task RevokeAsync(string value, CancellationToken cancellationToken);

    Task RevokeAllAsync(Guid userId, CancellationToken cancellationToken);
}

public interface IAdminBootstrapper
{
    Task BootstrapAsync(string userName, string password, CancellationToken cancellationToken);
}

public interface IDocumentAccessDal
{
    Task<IReadOnlyList<Guid>> GetAccessibleDocumentIdsAsync(
        Guid userId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken);

    Task ReplacePermissionsAsync(
        Guid documentId,
        Guid actorId,
        bool isAdmin,
        IReadOnlyCollection<Guid> userIds,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken);
}
