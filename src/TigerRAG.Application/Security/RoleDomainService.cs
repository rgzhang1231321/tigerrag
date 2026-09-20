using System.Text.RegularExpressions;

namespace TigerRAG.Application.Security;

/// <summary>角色名格式与保留集判定的纯静态规则。供 Application 服务复用，不依赖任何基础设施类型。</summary>
public static class RoleDomainService
{
    /// <summary>PascalCase、字母数字、长度 2–64；与 IdentityRole.Name 容量匹配，禁下划线/点/空格以避免与 permission code 混淆。</summary>
    public static readonly Regex NameFormat = new(
        @"^[A-Z][A-Za-z0-9]{1,63}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>校验角色名格式，违反则抛 <see cref="ArgumentException"/>。</summary>
    public static void EnsureName(string name)
    {
        if (string.IsNullOrEmpty(name) || !NameFormat.IsMatch(name))
        {
            throw new ArgumentException(
                $"角色名格式不合法：{name}。必须以大写字母开头，仅含字母与数字，长度 2–64。",
                nameof(name));
        }
    }

    /// <summary>是否属于 5 个系统保留名之一。保留名禁删禁重建。</summary>
    public static bool IsReserved(string name) => SystemRoles.All.Contains(name);

    /// <summary>是否是不可删除的 Admin 系统角色。前端 <c>permissions.ts</c> 编译期联合类型与后端 <c>[Authorize(Roles = "Admin")]</c> 强依赖。</summary>
    public static bool IsAdmin(string name) => string.Equals(name, SystemRoles.Admin, StringComparison.Ordinal);
}
