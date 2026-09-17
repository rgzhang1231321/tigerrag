using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TigerRAG.Application.Security;

namespace TigerRAG.Infrastructure.Identity;

/// <summary>JWT 签发器实现。基于 HS256 对称密钥；密钥长度不达标时 fail-fast。</summary>
public sealed class JwtAccessTokenIssuer(IOptions<JwtOptions> options) : IAccessTokenIssuer
{
    public AccessToken Issue(UserAccount user)
    {
        var settings = options.Value;
        // HS256 安全基线：密钥 ≥ 32 字节；启动期 AddTigerRagApi 已校验，运行时再保底一次。
        if (Encoding.UTF8.GetByteCount(settings.SigningKey) < 32)
        {
            throw new InvalidOperationException("Jwt:SigningKey must contain at least 32 bytes.");
        }

        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(settings.AccessTokenMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.UserName),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName)
        };
        claims.AddRange(user.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
        // permission claim 由服务端按 RolePermissionMap 聚合写入，前端不得自行从 role 推导。
        var permissions = RolePermissionMap.PermissionsFor(user.Roles);
        claims.AddRange(permissions.Select(permission => new Claim("permission", permission)));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            settings.Issuer,
            settings.Audience,
            claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
