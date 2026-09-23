using TigerRAG.Application.Users;

namespace TigerRAG.Application.Auth;

/// <summary>刷新会话生命周期 DAL 端口；创建/轮换/撤销/全量撤销。</summary>
public interface IRefreshSessionDal
{
    Task<RefreshToken> CreateAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>校验并轮换：原会话被撤销，返回带新令牌的新会话。</summary>
    Task<RefreshSession?> RotateAsync(string value, CancellationToken cancellationToken);

    Task RevokeAsync(string value, CancellationToken cancellationToken);

    /// <summary>用于改密/重置密码后强制全设备下线。</summary>
    Task RevokeAllAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>根据未撤销的 refresh token 查找关联用户；token 不存在或已撤销返回 null。用于退出登录审计。</summary>
    Task<UserAccount?> GetUserByTokenAsync(string value, CancellationToken cancellationToken);
}