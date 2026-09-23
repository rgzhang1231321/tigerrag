using Microsoft.EntityFrameworkCore;
using TigerRAG.Application.KnowledgeBases;
using TigerRAG.Infrastructure.Persistence;

namespace TigerRAG.Infrastructure.KnowledgeBases.Dal;

/// <summary>用户名称查询实现；从 Identity 用户表取 UserName。</summary>
public sealed class UserLookup(TigerRagDbContext dbContext) : IUserLookup
{
    public async Task<string?> GetUserNameAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await dbContext.Users
            .Where(u => u.Id == userId)
            .Select(u => u.UserName)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
