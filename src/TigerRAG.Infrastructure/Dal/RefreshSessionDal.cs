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
    IOptions<RefreshTokenOptions> options,
    IUnitOfWork unitOfWork) : IRefreshSessionDal
{
    public async Task<RefreshToken> CreateAsync(Guid userId, CancellationToken cancellationToken)
    {
        // 拿到用户当前 stamp 并写入新令牌，使后续轮换能基于 stamp 比对识别"敏感动作后的废止"。
        // Identity 在 CreateAsync / ChangePasswordAsync / SetLockoutEndDateAsync 等路径上会轮换 stamp，
        // 此处只读不写，因此 user 字段在 manager 内已被加载到本地，直接读属性即可，无额外往返。
        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new KeyNotFoundException($"User {userId} was not found.");
        var token = NewToken();
        dbContext.RefreshTokens.Add(Record(user.Id, token, user.SecurityStamp));
        await dbContext.SaveChangesAsync(cancellationToken);
        return token;
    }

    public async Task<RefreshSession?> RotateAsync(string value, CancellationToken cancellationToken)
    {
        var hash = Hash(value);
        var now = DateTimeOffset.UtcNow;

        RefreshSession? result = null;
        await unitOfWork.ExecuteAsync(async ct =>
        {
            // 行级锁：在同一事务里查 + 锁定 + 撤销 + 签发，避免两个并发请求拿到同一行。
            // 默认 READ COMMITTED 隔离下 SELECT FOR UPDATE 会阻塞其他写者，直到本事务提交/回滚。
            var current = await dbContext.RefreshTokens
                .FromSqlInterpolated($"""
                    SELECT * FROM refresh_token_record
                    WHERE "TokenHash" = {hash} AND "RevokedAt" IS NULL AND "ExpiresAt" > {now}
                    FOR UPDATE
                    """)
                .FirstOrDefaultAsync(ct);
            if (current is null)
            {
                return;
            }

            var user = await userManager.FindByIdAsync(current.UserId.ToString());
            if (user is null)
            {
                return;
            }

            // 锁定即拒绝：Identity 自动锁定（失败次数超阈值）或管理员手动锁定后，
            // 既未撤销该行的 RefreshToken 也不能再借此换取新 AccessToken，否则等于绕过锁口。
            if (user.LockoutEnd is { } lockoutEnd && lockoutEnd > now)
            {
                return;
            }

            // stamp 不一致即视为已被敏感动作废止：改密、角色变更、删除前的标记轮换等都会换 stamp。
            // SecurityStamp 是"按用户撤权"的主防线，比依赖 RevokeAllAsync 的批量 UPDATE 更早命中，
            // 也避免 PostgreSQL read-committed 下 ExecuteUpdate 快照与并发轮换 INSERT 之间的窗口。
            if (!string.Equals(current.SecurityStamp, user.SecurityStamp, StringComparison.Ordinal))
            {
                return;
            }

            // 原子轮换：原会话立即撤销，再签发新会话，避免并发请求复用旧令牌。
            current.RevokedAt = now;
            var replacement = NewToken();
            dbContext.RefreshTokens.Add(Record(user.Id, replacement, user.SecurityStamp));
            await dbContext.SaveChangesAsync(ct);

            var roles = await userManager.GetRolesAsync(user);
            result = new RefreshSession(
                new UserAccount(user.Id, user.UserName ?? string.Empty, roles.ToArray())
                {
                    SecurityStamp = user.SecurityStamp ?? string.Empty
                },
                replacement);
        }, cancellationToken);

        return result;
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

    private static refresh_token_record Record(Guid userId, RefreshToken token, string? securityStamp) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        TokenHash = Hash(token.Value),
        ExpiresAt = token.ExpiresAt,
        CreatedAt = DateTimeOffset.UtcNow,
        SecurityStamp = securityStamp
    };

    // 哈希而非加密：哈希不可逆，泄露库也不会让持有者伪造会话。
    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
