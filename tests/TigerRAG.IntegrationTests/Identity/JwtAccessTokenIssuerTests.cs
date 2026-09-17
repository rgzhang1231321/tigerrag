using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using TigerRAG.Application.Security;
using TigerRAG.Infrastructure.Identity;

namespace TigerRAG.IntegrationTests.Identity;

/// <summary>锁定 JWT 同时携带 role 与 permission claim 的契约；permission 由服务端按 RolePermissionMap 聚合写入。</summary>
public sealed class JwtAccessTokenIssuerTests
{
    [Fact]
    public void Issue_EmitsRoleAndPermissionClaims()
    {
        var issuer = CreateIssuer();
        var user = new UserAccount(Guid.NewGuid(), "editor", [SystemRoles.Editor]);

        var token = issuer.Issue(user);

        var handler = new JwtSecurityTokenHandler();
        var parsed = handler.ReadJwtToken(token.Value);

        var roles = parsed.Claims
            .Where(claim => claim.Type == ClaimTypes.Role)
            .Select(claim => claim.Value)
            .ToList();
        Assert.Equal([SystemRoles.Editor], roles);

        var permissions = parsed.Claims
            .Where(claim => claim.Type == "permission")
            .Select(claim => claim.Value)
            .ToHashSet();
        Assert.Equal(
            new HashSet<string>([SystemPermissions.ManageDocuments, SystemPermissions.UseChat]),
            permissions);
    }

    [Fact]
    public void Issue_ForAdmin_EmitsAllPermissions()
    {
        var issuer = CreateIssuer();
        var user = new UserAccount(Guid.NewGuid(), "admin", [SystemRoles.Admin]);

        var token = issuer.Issue(user);

        var handler = new JwtSecurityTokenHandler();
        var parsed = handler.ReadJwtToken(token.Value);
        var permissions = parsed.Claims
            .Where(claim => claim.Type == "permission")
            .Select(claim => claim.Value)
            .ToHashSet();
        Assert.Equal(new HashSet<string>(SystemPermissions.All), permissions);
    }

    [Fact]
    public void Issue_PreservesSubAndNameClaims()
    {
        var issuer = CreateIssuer();
        var userId = Guid.NewGuid();
        var user = new UserAccount(userId, "viewer", [SystemRoles.Viewer]);

        var token = issuer.Issue(user);

        var handler = new JwtSecurityTokenHandler();
        var parsed = handler.ReadJwtToken(token.Value);
        Assert.Equal(userId.ToString(), parsed.Subject);
        Assert.Equal("viewer", parsed.Claims.First(claim => claim.Type == ClaimTypes.Name).Value);
    }

    private static JwtAccessTokenIssuer CreateIssuer() => new(Options.Create(new JwtOptions
    {
        Issuer = "TigerRAG.Tests",
        Audience = "TigerRAG.Tests",
        SigningKey = "test-only-signing-key-with-at-least-32-characters",
        AccessTokenMinutes = 15
    }));
}