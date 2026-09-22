using System.Text.RegularExpressions;

namespace TigerRAG.Application.Roles;

/// <summary>角色名格式与基础校验的纯静态规则。供 Application 服务复用，不依赖任何基础设施类型。</summary>
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
}
