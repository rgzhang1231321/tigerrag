namespace TigerRAG.Api.Controllers.Users;

using TigerRAG.Application.Users;

/// <summary>用户视图：登录响应 / 用户列表接口下发给前端的统一形态。</summary>
public sealed record UserResponse(Guid Id, string UserName, IReadOnlyList<string> Roles, bool IsLocked)
{
    public static UserResponse From(UserListItem user) => new(user.Id, user.UserName, user.Roles, user.IsLocked);
    public static UserResponse From(UserAccount user) => new(user.Id, user.UserName, user.Roles, false);
}