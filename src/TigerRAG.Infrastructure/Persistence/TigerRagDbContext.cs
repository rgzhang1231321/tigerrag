using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TigerRAG.Application.Security;
using TigerRAG.Infrastructure.Identity;
using TigerRAG.Infrastructure.Persistence.Entities;

namespace TigerRAG.Infrastructure.Persistence;

public sealed class TigerRagDbContext(DbContextOptions<TigerRagDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<KnowledgeBaseRecord> KnowledgeBases => Set<KnowledgeBaseRecord>();
    public DbSet<DocumentRecord> Documents => Set<DocumentRecord>();
    public DbSet<DocumentPermissionRecord> DocumentPermissions => Set<DocumentPermissionRecord>();
    public DbSet<DocumentChunkRecord> DocumentChunks => Set<DocumentChunkRecord>();
    public DbSet<ConversationRecord> Conversations => Set<ConversationRecord>();
    public DbSet<MessageRecord> Messages => Set<MessageRecord>();
    public DbSet<AuditLogRecord> AuditLogs => Set<AuditLogRecord>();
    public DbSet<RefreshTokenRecord> RefreshTokens => Set<RefreshTokenRecord>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<AppUser>().Property(user => user.CreatedAt).IsRequired();

        ConfigureKnowledge(builder);
        ConfigureConversations(builder);
        ConfigureAudit(builder);
        ConfigureRefreshTokens(builder);
        SeedRoles(builder);
    }

    private static void ConfigureKnowledge(ModelBuilder builder)
    {
        builder.Entity<KnowledgeBaseRecord>(entity =>
        {
            entity.ToTable("KnowledgeBases");
            entity.Property(value => value.Name).HasMaxLength(200);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(value => value.OwnerId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<DocumentRecord>(entity =>
        {
            entity.ToTable("Documents");
            entity.Property(value => value.FileName).HasMaxLength(500);
            entity.Property(value => value.StoragePath).HasMaxLength(1000);
            entity.Property(value => value.Status).HasConversion<string>().HasMaxLength(32);
            entity.HasOne<KnowledgeBaseRecord>().WithMany().HasForeignKey(value => value.KnowledgeBaseId);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(value => value.CreatedBy).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(value => new { value.KnowledgeBaseId, value.Status });
        });

        builder.Entity<DocumentPermissionRecord>(entity =>
        {
            entity.ToTable("DocumentPermissions");
            entity.HasKey(value => new { value.DocumentId, value.PrincipalType, value.PrincipalId });
            entity.Property(value => value.PrincipalType).HasConversion<string>().HasMaxLength(16);
            entity.HasOne<DocumentRecord>().WithMany().HasForeignKey(value => value.DocumentId);
            entity.HasIndex(value => new { value.PrincipalType, value.PrincipalId });
        });

        builder.Entity<DocumentChunkRecord>(entity =>
        {
            entity.ToTable("DocumentChunks");
            entity.HasOne<DocumentRecord>().WithMany().HasForeignKey(value => value.DocumentId);
            entity.HasIndex(value => new { value.DocumentId, value.Position }).IsUnique();
            entity.HasIndex(value => value.SearchVector).HasMethod("GIN");
        });
    }

    private static void ConfigureConversations(ModelBuilder builder)
    {
        builder.Entity<ConversationRecord>(entity =>
        {
            entity.ToTable("Conversations");
            entity.Property(value => value.Title).HasMaxLength(300);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(value => value.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<MessageRecord>(entity =>
        {
            entity.ToTable("Messages");
            entity.Property(value => value.Role).HasMaxLength(32);
            entity.HasOne<ConversationRecord>().WithMany().HasForeignKey(value => value.ConversationId);
            entity.HasIndex(value => new { value.ConversationId, value.CreatedAt });
        });
    }

    private static void ConfigureAudit(ModelBuilder builder)
    {
        builder.Entity<AuditLogRecord>(entity =>
        {
            entity.ToTable("AuditLogs");
            entity.Property(value => value.IpAddress).HasMaxLength(64);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(value => value.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(value => value.CreatedAt);
        });
    }

    private static void ConfigureRefreshTokens(ModelBuilder builder)
    {
        builder.Entity<RefreshTokenRecord>(entity =>
        {
            entity.ToTable("RefreshTokens");
            entity.Property(value => value.TokenHash).HasMaxLength(64);
            entity.HasIndex(value => value.TokenHash).IsUnique();
            entity.HasIndex(value => new { value.UserId, value.RevokedAt });
            entity.HasOne<AppUser>().WithMany().HasForeignKey(value => value.UserId);
        });
    }

    private static void SeedRoles(ModelBuilder builder)
    {
        var roles = new[]
        {
            Role("8f86fa4c-c8e5-4bc0-a563-528b70a74576", SystemRoles.Admin),
            Role("1f0187a1-2897-4bf8-ab73-27fa1a2e4c76", SystemRoles.KbManager),
            Role("cb81efa4-2203-4f2b-88e4-1e18af3ba95c", SystemRoles.Editor),
            Role("eadf72f7-d444-43cc-9fd9-5aa320640274", SystemRoles.Viewer),
            Role("9081bc7b-1762-43cf-8649-bc30ed21ec52", SystemRoles.Auditor)
        };

        builder.Entity<IdentityRole<Guid>>().HasData(roles);
    }

    private static IdentityRole<Guid> Role(string id, string name) => new()
    {
        Id = Guid.Parse(id),
        Name = name,
        NormalizedName = name.ToUpperInvariant(),
        ConcurrencyStamp = id
    };
}
