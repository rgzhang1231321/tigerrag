namespace TigerRAG.Application.Users;

/// <summary>
/// 用户列表行视图：在 <see cref="UserAccount"/> 基础上追加锁口状态，供前端展示与开关。
/// <c>SecurityStamp</c> 在管理页列表里通常不展示，但同一份 DAL 映射复用 <see cref="UserAccount"/> 的字端。
/// </summary>
public sealed record UserListItem(Guid Id, string UserName, IReadOnlyList<string> Roles, bool IsLocked)
{
    public string SecurityStamp { get; init; } = string.Empty;
}