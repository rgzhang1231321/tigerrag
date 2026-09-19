using Microsoft.EntityFrameworkCore;
using TigerRAG.Application.Security;
using TigerRAG.Infrastructure.Persistence;

namespace TigerRAG.Infrastructure.Dal;

/// <summary>
/// 基于 EF Core 共享 DbContext 的事务边界实现。
/// 同一作用域内的 DAL 写操作都走同一个 <see cref="TigerRagDbContext"/>，
/// 通过本类型把多步写入包在单一事务内；任一阶段失败即整体回滚。
/// </summary>
public sealed class EfUnitOfWork(TigerRagDbContext dbContext) : IUnitOfWork
{
    public async Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        // 嵌套调用时（如 RefreshSessionDal.RotateAsync 已在事务内），
        // 复用已有事务，避免 BEGIN 在已开启事务的连接上抛 InvalidOperationException。
        var hasExistingTransaction = dbContext.Database.CurrentTransaction is not null;
        if (hasExistingTransaction)
        {
            await operation(cancellationToken);
            return;
        }

        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await operation(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
