namespace TigerRAG.Application.Users;

/// <summary>用户查询与角色管理的 DAL 端口。</summary>
public interface IUserDal
{
    /// <summary>
    /// 校验客户端提交的密码哈希（<c>MD5(password+salt)</c>）；服务端会用 DB 中的 salt 拼接后交 Identity 走 PBKDF2。
    /// 失败返回 null（不区分原因，防止枚举攻击）。
    /// </summary>
    Task<UserAccount?> ValidateCredentialsAsync(
        string userName,
        string passwordHash,
        CancellationToken cancellationToken);

    /// <summary>取用户的密码盐值；用户不存在时返回 null。该 salt 必须来自 DB，绝不信任客户端。</summary>
    Task<string?> GetPasswordSaltAsync(string userName, CancellationToken cancellationToken);

    Task<IReadOnlyList<UserListItem>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 按 Id 取当前 stamp 与锁口状态，供 JWT 撤权校验器使用；用户不存在返回 null。
    /// 该路径不走 EF ChangeTracker 之外的状态，与管理页 <c>ListAsync</c> 复用同一映射。
    /// </summary>
    Task<RevocationSnapshot?> GetRevocationSnapshotAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task AssignRolesAsync(
        Guid userId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken);
}