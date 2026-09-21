using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using TigerRAG.Application.Shared;
using TigerRAG.Application.Users;
using TigerRAG.Infrastructure.Auth;
using TigerRAG.Infrastructure.Identity;

namespace TigerRAG.IntegrationTests.Identity;

/// <summary>锁定 JWT 携带 role claim 的契约；权限通路为 角色 → 菜单，不再下发 permission claim。</summary>
public sealed class JwtAccessTokenIssuerTests
{
    [Fact]
    public async Task IssueAsync_EmitsRoleClaimsForEachRole()
    {
        var issuer = CreateIssuer();
        var user = new UserAccount(Guid.NewGuid(), "editor", [SystemRoles.Editor, SystemRoles.Viewer]);

        var token = await issuer.IssueAsync(user, CancellationToken.None);

        var handler = new JwtSecurityTokenHandler();
        var parsed = handler.ReadJwtToken(token.Value);

        var roles = parsed.Claims
            .Where(claim => claim.Type == ClaimTypes.Role)
            .Select(claim => claim.Value)
            .ToList();
        Assert.Equal([SystemRoles.Editor, SystemRoles.Viewer], roles);

        // 不再下发 permission claim：前端不得从 role 推导权限。
        Assert.DoesNotContain(parsed.Claims, claim => claim.Type == "permission");
    }

    [Fact]
    public async Task IssueAsync_PreservesSubAndNameClaims()
    {
        var issuer = CreateIssuer();
        var userId = Guid.NewGuid();
        var user = new UserAccount(userId, "viewer", [SystemRoles.Viewer]);

        var token = await issuer.IssueAsync(user, CancellationToken.None);

        var parsed = handler.ReadJwtToken(token.Value);
        Assert.Equal(userId.ToString(), parsed.Subject);
        Assert.Equal("viewer", parsed.Claims.First(claim => claim.Type == ClaimTypes.Name).Value);
    }

    [Fact]
    public async Task IssueAsync_EmitsSecurityStampClaim()
    {
        var issuer = CreateIssuer();
        var user = new UserAccount(Guid.NewGuid(), "viewer", [SystemRoles.Viewer]) { SecurityStamp = "stamp-xyz" };

        var token = await issuer.IssueAsync(user, CancellationToken.None);

        var parsed = handler.ReadJwtToken(token.Value);
        // security_stamp 是按用户撤权的比对基准；缺它就拒绝，是部署后强制重登的关键。
        var stamp = parsed.Claims.FirstOrDefault(claim => claim.Type == "security_stamp")?.Value;
        Assert.Equal("stamp-xyz", stamp);
    }

    [Fact]
    public async Task IssueAsync_EmitsUniqueJtiClaim()
    {
        var issuer = CreateIssuer();
        var user = new UserAccount(Guid.NewGuid(), "viewer", [SystemRoles.Viewer]);

        var first = handler.ReadJwtToken((await issuer.IssueAsync(user, CancellationToken.None)).Value);
        var second = handler.ReadJwtToken((await issuer.IssueAsync(user, CancellationToken.None)).Value);

        var jtiA = first.Claims.First(claim => claim.Type == JwtRegisteredClaimNames.Jti).Value;
        var jtiB = second.Claims.First(claim => claim.Type == JwtRegisteredClaimNames.Jti).Value;
        // jti 每次签发都不同：避免两个 token 撞 id，也便于以后扩展按 jti 黑名单。
        Assert.False(string.IsNullOrWhiteSpace(jtiA));
        Assert.NotEqual(jtiA, jtiB);
    }

    private static readonly JwtSecurityTokenHandler handler = new();

    private static JwtAccessTokenIssuer CreateIssuer() => new(
        Options.Create(new JwtOptions
        {
            Issuer = "TigerRAG.Tests",
            Audience = "TigerRAG.Tests",
            SigningKey = "test-only-signing-key-with-at-least-32-characters",
            AccessTokenMinutes = 15
        }));
}
