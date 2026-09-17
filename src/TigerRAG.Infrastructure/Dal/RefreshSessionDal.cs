using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TigerRAG.Application.Security;
using TigerRAG.Infrastructure.Identity;
using TigerRAG.Infrastructure.Persistence;
using TigerRAG.Infrastructure.Persistence.Entities;

namespace TigerRAG.Infrastructure.Dal;

/// <summary>刷新令牌会话 DAL。原值只出现在 Cookie，库内仅存 SHA-256 哈希。</summary>
public sealed class RefreshSessionDal(
    TigerRagDbContext dbContext,
    UserManager<AppUser> userManager,
    IOptions<RefreshTokenOptions> options) : IRefreshSessionDal
{
    public async Task<RefreshToken> CreateAsync(Guid userId, CancellationToken cancellationToken)
    {
        var token = NewToken();
        dbContext.RefreshTokens.Add(Record(userId, token));
        await dbContext.SaveChangesAsync(cancellationToken);
        return token;
    }

    public async Task<RefreshSession?> RotateAsync(string value, CancellationToken cancellationToken)
    {
        var hash = Hash(value);
        var now = DateTimeOffset.UtcNow;
        // 行级锁：在同一事务里查 + 锁定 + 撤销 + 签发，避免两个并发请求拿到同一行。
        // 默认 READ COMMITTED 隔离下 SELECT FOR UPDATE 会阻塞其他写者，直到本事务提交/回滚。
        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var current = await dbContext.RefreshTokens
            .FromSqlInterpolated($"""
                SELECT * FROM refresh_token_record
                WHERE "TokenHash" = {hash} AND "RevokedAt" IS NULL AND "ExpiresAt" > {now}
                FOR UPDATE
                """)
            .FirstOrDefaultAsync(cancellationToken);
        if (current is null)
        {
            await tx.RollbackAsync(cancellationToken);
            return null;
        }

        var user = await userManager.FindByIdAsync(current.UserId.ToString());
        if (user is null)
        {
            await tx.RollbackAsync(cancellationToken);
            return null;
        }

        // 原子轮换：原会话立即撤销，再签发新会话，避免并发请求复用旧令牌。
        current.RevokedAt = now;
        var replacement = NewToken();
        dbContext.RefreshTokens.Add(Record(user.Id, replacement));
        await dbContext.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        var roles = await userManager.GetRolesAsync(user);
        return new RefreshSession(
            new UserAccount(user.Id, user.UserName ?? string.Empty, roles.ToArray()),
            replacement);
    }

    public async Task RevokeAsync(string value, CancellationToken cancellationToken)
    {
        var hash = Hash(value);
        var token = await dbContext.RefreshTokens
            .SingleOrDefaultAsync(item => item.TokenHash == hash && item.RevokedAt == null, cancellationToken);
        if (token is null)
        {
            return;
        }
        
        token.RevokedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeAllAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await dbContext.RefreshTokens
            .Where(token => token.UserId == userId && token.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(token => token.RevokedAt, now),
                cancellationToken);
    }

    private RefreshToken NewToken() => new(
        Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32)),
        DateTimeOffset.UtcNow.AddDays(options.Value.LifetimeDays));

    private static refresh_token_record Record(Guid userId, RefreshToken token) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        TokenHash = Hash(token.Value),
        ExpiresAt = token.ExpiresAt,
        CreatedAt = DateTimeOffset.UtcNow
    };

    // 哈希而非加密：哈希不可逆，泄露库也不会让持有者伪造会话。
    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
