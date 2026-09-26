using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TigerRAG.Application.Documents.Lifecycle;
using TigerRAG.Domain.Documents;
using TigerRAG.Infrastructure.Documents.Dal;
using TigerRAG.Infrastructure.Identity;
using TigerRAG.Infrastructure.Persistence;
using TigerRAG.Infrastructure.Persistence.Entities.Documents;
using TigerRAG.Infrastructure.Persistence.Entities.KnowledgeBases;

namespace TigerRAG.IntegrationTests.Infrastructure;

/// <summary>DocumentLifecycleDal 集成测试：TryClaim 原子认领、并发认领仅一人胜出、ResetProcessingToPending。</summary>
[Collection(nameof(PostgresCollection))]
public sealed class DocumentLifecycleDalTests : IAsyncLifetime
{
    private const string TestDatabaseName = "tigerrag_lifecycle_test";
    private static readonly string AdminConnectionString =
        "Host=localhost;Port=5432;Database=postgres;Username=tigerrag;Password=And@2088;Timeout=5";
    private static readonly string TestConnectionString =
        $"Host=localhost;Port=5432;Database={TestDatabaseName};Username=tigerrag;Password=And@2088;Timeout=5";

    private ServiceProvider _rootProvider = null!;
    private Guid _seedUserId;
    private Guid _seedKbId;

    public async Task InitializeAsync()
    {
        if (!await LocalPostgresFixture.IsAvailableAsync())
            throw new InvalidOperationException("Local Postgres is not reachable.");

        Npgsql.NpgsqlConnection.ClearAllPools();
        await DropTestDatabaseAsync();
        await CreateTestDatabaseAsync();
        _rootProvider = BuildServiceProvider(TestConnectionString);
        await EnsureSchemaAsync(_rootProvider);
        _seedUserId = await SeedUserAsync();
        _seedKbId = await SeedKbAsync();
    }

    public async Task DisposeAsync()
    {
        if (_rootProvider is not null) await _rootProvider.DisposeAsync();
        await DropTestDatabaseAsync();
    }

    /// <summary>TryClaimAsync：Pending 文档认领成功返回 true，状态变为 Processing。</summary>
    [Fact]
    public async Task TryClaimAsync_PendingDocument_ReturnsTrueAndSetsProcessing()
    {
        await using var scope = _rootProvider.CreateAsyncScope();
        var dal = scope.ServiceProvider.GetRequiredService<IDocumentLifecycleDal>();
        var db = scope.ServiceProvider.GetRequiredService<TigerRagDbContext>();

        var docId = Guid.NewGuid();
        await SeedDocumentAsync(db, docId, DocumentStatus.Pending);

        var result = await dal.TryClaimAsync(docId, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.True(result);
        // 用新 scope 验证，避免 EF 缓存旧状态干扰。
        await using var verifyScope = _rootProvider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<TigerRagDbContext>();
        var record = await verifyDb.Documents.FirstAsync(d => d.Id == docId);
        Assert.Equal(DocumentStatus.Processing, record.Status);
    }

    /// <summary>TryClaimAsync：Processing 文档认领失败返回 false（WHERE Status=Pending 不匹配）。</summary>
    [Fact]
    public async Task TryClaimAsync_ProcessingDocument_ReturnsFalse()
    {
        await using var scope = _rootProvider.CreateAsyncScope();
        var dal = scope.ServiceProvider.GetRequiredService<IDocumentLifecycleDal>();
        var db = scope.ServiceProvider.GetRequiredService<TigerRagDbContext>();

        var docId = Guid.NewGuid();
        await SeedDocumentAsync(db, docId, DocumentStatus.Processing);

        var result = await dal.TryClaimAsync(docId, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.False(result);
    }

    /// <summary>TryClaimAsync：并发认领仅一人胜出。两个线程同时认领同一 Pending 文档，只有一个返回 true。</summary>
    [Fact]
    public async Task TryClaimAsync_ConcurrentClaim_OnlyOneWins()
    {
        await using var scope = _rootProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TigerRagDbContext>();
        var docId = Guid.NewGuid();
        await SeedDocumentAsync(db, docId, DocumentStatus.Pending);

        var task1 = ClaimAsync(docId);
        var task2 = ClaimAsync(docId);
        var results = await Task.WhenAll(task1, task2);

        Assert.True(results.Count(r => r) == 1, $"Expected exactly 1 true, got {results.Count(r => r)}");

        await using var verifyScope = _rootProvider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<TigerRagDbContext>();
        var record = await verifyDb.Documents.FirstAsync(d => d.Id == docId);
        Assert.Equal(DocumentStatus.Processing, record.Status);
    }

    /// <summary>ResetProcessingToPendingAsync：Processing 文档重置为 Pending。</summary>
    [Fact]
    public async Task ResetProcessingToPendingAsync_ProcessingDocument_ReturnsTrueAndResets()
    {
        await using var scope = _rootProvider.CreateAsyncScope();
        var dal = scope.ServiceProvider.GetRequiredService<IDocumentLifecycleDal>();
        var db = scope.ServiceProvider.GetRequiredService<TigerRagDbContext>();

        var docId = Guid.NewGuid();
        await SeedDocumentAsync(db, docId, DocumentStatus.Processing);

        var result = await dal.ResetProcessingToPendingAsync(docId, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.True(result);
        await using var verifyScope = _rootProvider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<TigerRagDbContext>();
        var record = await verifyDb.Documents.FirstAsync(d => d.Id == docId);
        Assert.Equal(DocumentStatus.Pending, record.Status);
        Assert.Null(record.FailureReason);
    }

    /// <summary>ResetProcessingToPendingAsync：Pending 文档重置失败返回 false。</summary>
    [Fact]
    public async Task ResetProcessingToPendingAsync_PendingDocument_ReturnsFalse()
    {
        await using var scope = _rootProvider.CreateAsyncScope();
        var dal = scope.ServiceProvider.GetRequiredService<IDocumentLifecycleDal>();
        var db = scope.ServiceProvider.GetRequiredService<TigerRagDbContext>();

        var docId = Guid.NewGuid();
        await SeedDocumentAsync(db, docId, DocumentStatus.Pending);

        var result = await dal.ResetProcessingToPendingAsync(docId, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.False(result);
    }

    // 屏蔽：避免上下文失效时误清理 document_permission_record 表数据
    ///// <summary>DeleteAsync + DeleteChunksAsync + DeletePermissionsAsync：级联清理。</summary>
    // [Fact]
    // public async Task DeleteCascade_RemovesDocumentAndRelatedRecords()
    // {
    //     await using var scope = _rootProvider.CreateAsyncScope();
    //     var dal = scope.ServiceProvider.GetRequiredService<IDocumentLifecycleDal>();
    //     var db = scope.ServiceProvider.GetRequiredService<TigerRagDbContext>();

    //     var docId = Guid.NewGuid();
    //     await SeedDocumentAsync(db, docId, DocumentStatus.Indexed);
    //     await SeedChunkAsync(db, docId, 0);
    //     await SeedChunkAsync(db, docId, 1);
    //     await SeedPermissionAsync(db, docId, Guid.NewGuid());

    //     await dal.DeleteChunksAsync(docId, CancellationToken.None);
    //     await dal.DeletePermissionsAsync(docId, CancellationToken.None);
    //     await dal.DeleteAsync(docId, CancellationToken.None);

    //     Assert.Empty(db.DocumentChunks.Where(c => c.DocumentId == docId));
    //     Assert.Empty(db.DocumentPermissions.Where(p => p.DocumentId == docId));
    //     Assert.Empty(db.Documents.Where(d => d.Id == docId));
    // }

    private async Task<bool> ClaimAsync(Guid docId)
    {
        await using var scope = _rootProvider.CreateAsyncScope();
        var dal = scope.ServiceProvider.GetRequiredService<IDocumentLifecycleDal>();
        return await dal.TryClaimAsync(docId, DateTimeOffset.UtcNow, CancellationToken.None);
    }

    private async Task<Guid> SeedUserAsync()
    {
        await using var scope = _rootProvider.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = $"lifecycle-user-{Guid.NewGuid():N}",
            PasswordSalt = "salt",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var result = await userManager.CreateAsync(user, "TestPassword123!");
        if (!result.Succeeded)
            throw new InvalidOperationException("Seed user failed: " + string.Join("; ", result.Errors.Select(e => e.Description)));
        return user.Id;
    }

    private async Task<Guid> SeedKbAsync()
    {
        await using var scope = _rootProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TigerRagDbContext>();
        var kbId = Guid.NewGuid();
        await db.KnowledgeBases.AddAsync(new knowledge_base_record
        {
            Id = kbId,
            Name = $"test-kb-{Guid.NewGuid():N}",
            Description = null,
            OwnerId = _seedUserId,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return kbId;
    }

    private async Task SeedDocumentAsync(TigerRagDbContext db, Guid id, DocumentStatus status)
    {
        await db.Documents.AddAsync(new document_record
        {
            Id = id,
            KnowledgeBaseId = _seedKbId,
            FileName = "test.txt",
            StoragePath = "kb/test/doc.txt",
            MimeType = "text/plain",
            Status = status,
            ChunkCount = 0,
            FailureReason = null,
            Size = 1024,
            CreatedBy = _seedUserId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedChunkAsync(TigerRagDbContext db, Guid docId, int position)
    {
        await db.DocumentChunks.AddAsync(new document_chunk_record
        {
            Id = Guid.NewGuid(),
            DocumentId = docId,
            Position = position,
            PageNumber = null,
            Title = null,
            Content = "chunk content",
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedPermissionAsync(TigerRagDbContext db, Guid docId, Guid principalId)
    {
        await db.DocumentPermissions.AddAsync(new document_permission_record
        {
            DocumentId = docId,
            PrincipalType = PermissionPrincipalType.User,
            PrincipalId = principalId,
        });
        await db.SaveChangesAsync();
    }

    private static ServiceProvider BuildServiceProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddDbContext<TigerRagDbContext>(options => options.UseNpgsql(connectionString));
        services.AddAuthentication();
        services
            .AddIdentityCore<AppUser>(options =>
            {
                options.Password.RequiredLength = 5;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireDigit = false;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddSignInManager()
            .AddEntityFrameworkStores<TigerRagDbContext>();
        services.AddScoped<TigerRagDbContext>();
        services.AddScoped<IDocumentLifecycleDal, DocumentLifecycleDal>();
        return services.BuildServiceProvider();
    }

    private static async Task EnsureSchemaAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TigerRagDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    private static async Task CreateTestDatabaseAsync()
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand($"CREATE DATABASE {TestDatabaseName} TEMPLATE template0", connection);
        try { await cmd.ExecuteNonQueryAsync(); }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "42P04") { /* 已存在则忽略 */ }
    }

    private static async Task DropTestDatabaseAsync()
    {
        try
        {
            await using var connection = new NpgsqlConnection(AdminConnectionString);
            await connection.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{TestDatabaseName}';" +
                $"DROP DATABASE IF EXISTS {TestDatabaseName};", connection);
            await cmd.ExecuteNonQueryAsync();
        }
        catch { /* 数据库可能不存在 */ }
    }
}
