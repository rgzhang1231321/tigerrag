namespace TigerRAG.Application.Auth;

/// <summary>
/// 轮换用户的按用户撤权纪元（Identity SecurityStamp）并把新值写进 JWT 撤权缓存。
/// 实现位于 Infrastructure，避免 Application 直接依赖 ASP.NET Core Identity。
/// 失败抛 <see cref="InvalidOperationException"/>，调用方应让其冒泡以中断正在进行的敏感动作。
/// </summary>
public interface IUserSecurityStampRotator
{
    Task RotateAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>用户删除时清空缓存项；防止 TTL 内已失效 stamp 仍命中。</summary>
    Task InvalidateAsync(Guid userId, CancellationToken cancellationToken);
}