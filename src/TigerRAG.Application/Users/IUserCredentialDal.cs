namespace TigerRAG.Application.Users;

/// <summary>
/// 用户凭据写操作的 DAL 端口。
/// 客户端提交的 <c>passwordHash</c> 是 <c>MD5(password+salt)</c>；
/// 服务端再用 DB 中的 salt 拼接后交给 Identity 走 PBKDF2 存储。
/// </summary>
public interface IUserCredentialDal
{
    /// <summary>
    /// 仅创建用户并赋角色，生成 salt 落库但不设置密码。
    /// 客户端随后用返回的 salt 计算 MD5 后调用 <see cref="SetInitialPasswordAsync"/>。
    /// </summary>
    Task<UserAccount> CreateAsync(
        string userName,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken);

    /// <summary>为刚创建的用户设置初始密码（<c>MD5(password+salt)</c>，salt 来自 DB）。</summary>
    Task SetInitialPasswordAsync(
        Guid userId,
        string passwordHash,
        CancellationToken cancellationToken);

    Task<bool> ChangePasswordAsync(
        Guid userId,
        string currentPasswordHash,
        string newPasswordHash,
        CancellationToken cancellationToken);

    Task ResetPasswordAsync(
        Guid userId,
        string newPasswordHash,
        CancellationToken cancellationToken);

    /// <summary>删除用户及其全部关联数据（凭据、角色绑定、刷新会话）。用户不存在时返回 false。</summary>
    Task<bool> DeleteAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// 设置用户锁口到期时间：<c>lockoutEnd</c> 为 null 表示解锁，为未来时间表示锁定。
    /// 被锁用户的 <c>CheckPasswordSignInAsync</c> 会直接失败，无法登录。
    /// </summary>
    Task SetLockoutAsync(Guid userId, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken);
}