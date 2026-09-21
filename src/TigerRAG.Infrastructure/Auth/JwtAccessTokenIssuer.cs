using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TigerRAG.Application.Auth;
using TigerRAG.Application.Users;

namespace TigerRAG.Infrastructure.Auth;

/// <summary>JWT 签发器实现。基于 HS256 对称密钥；密钥长度不达标时 fail-fast。</summary>
public sealed class JwtAccessTokenIssuer(
    IOptions<JwtOptions> options) : IAccessTokenIssuer
{
    public Task<AccessToken> IssueAsync(UserAccount user, CancellationToken cancellationToken)
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
            new(ClaimTypes.Name, user.UserName),
            // 按用户撤权：OnTokenValidated 把这里写入的 stamp 与缓存/DB 实时值比对，
            // 不一致即视为失效。空 stamp 意味着签发前未走 DAL 拉用户，落库时必填。
            new("security_stamp", user.SecurityStamp),
            // 单 token 唯一标识；为将来按会话精确撤权（"踢掉这一台设备"）留口子，
            // 当前未消费，但加成本极低，后续接 denylist 不需要重新签发存量 token。
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };
        claims.AddRange(user.Roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            settings.Issuer,
            settings.Audience,
            claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return Task.FromResult(new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt));
    }
}