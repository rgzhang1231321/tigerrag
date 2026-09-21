using TigerRAG.Application.Shared;

namespace TigerRAG.Api.Common;

/// <summary>
/// 密码哈希格式校验的 Controller 层共享入口。包 <see cref="PasswordHashFormat.EnsureAcceptable"/> 为 try/catch，
/// 失败时通过 <c>out string error</c> 返回 <see cref="PasswordHashFormat"/> 的错误文案，供 Controller 直接透传业务码 Validation。
/// </summary>
internal static class PasswordHashValidator
{
    /// <summary>校验失败返回 false 并通过 <paramref name="error"/> 带回原文；通过返回 true。</summary>
    public static bool TryValidate(string passwordHash, out string error)
    {
        try
        {
            PasswordHashFormat.EnsureAcceptable(passwordHash);
            error = string.Empty;
            return true;
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
