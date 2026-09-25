using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TigerRAG.Infrastructure.Identity;
using TigerRAG.Infrastructure.Persistence.Entities;
using TigerRAG.Infrastructure.Persistence.Entities.ApiLogs;
using TigerRAG.Infrastructure.Persistence.Entities.Auth;
using TigerRAG.Infrastructure.Persistence.Entities.Conversations;
using TigerRAG.Infrastructure.Persistence.Entities.Documents;
using TigerRAG.Infrastructure.Persistence.Entities.KnowledgeBases;
using TigerRAG.Infrastructure.Persistence.Entities.Menus;
using TigerRAG.Infrastructure.Persistence.Entities.OperationAudit;
using TigerRAG.Infrastructure.Persistence.Entities.RoleEndpointGrants;

namespace TigerRAG.Infrastructure.Persistence;

/// <summary>
/// TigerRAG 主 DbContext。仅运行时 ORM，不引入 EF Migration；DDL 由 deploy/sql/ 维护。
/// </summary>
public sealed class TigerRagDbContext(DbContextOptions<TigerRagDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<knowledge_base_record> KnowledgeBases => Set<knowledge_base_record>();
    public DbSet<knowledge_base_permission_record> KnowledgeBasePermissions => Set<knowledge_base_permission_record>();
    public DbSet<document_record> Documents => Set<document_record>();
    public DbSet<document_permission_record> DocumentPermissions => Set<document_permission_record>();
    public DbSet<document_chunk_record> DocumentChunks => Set<document_chunk_record>();
    public DbSet<conversation_record> Conversations => Set<conversation_record>();
    public DbSet<message_record> Messages => Set<message_record>();
    public DbSet<audit_log_record> AuditLogs => Set<audit_log_record>();
    public DbSet<refresh_token_record> RefreshTokens => Set<refresh_token_record>();
    public DbSet<menu_config_record> MenuConfigs => Set<menu_config_record>();
    public DbSet<api_log_record> ApiLogs => Set<api_log_record>();
    public DbSet<operation_audit_record> OperationAudits => Set<operation_audit_record>();
    public DbSet<role_endpoint_grant_record> RoleEndpointGrants => Set<role_endpoint_grant_record>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // 业务表名 = 实体类名（EF Core 默认约定），确保代码与数据库一一对应。
        builder.Entity<AppUser>(entity =>
        {
            entity.Property(user => user.PasswordSalt).HasMaxLength(64).IsRequired();
            entity.Property(user => user.CreatedAt).IsRequired();
        });

        ConfigureKnowledge(builder);
        ConfigureConversations(builder);
        ConfigureAudit(builder);
        ConfigureRefreshTokens(builder);
        ConfigureMenuConfigs(builder);
        ConfigureApiLogs(builder);
        ConfigureOperationAudits(builder);
        ConfigureRoleEndpointGrants(builder);
        SeedRoles(builder);
    }

    private static void ConfigureKnowledge(ModelBuilder builder)
    {
        builder.Entity<knowledge_base_record>(entity =>
        {
            entity.ToTable("knowledge_base_record");
            entity.Property(value => value.Name).HasMaxLength(200);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(value => value.OwnerId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<knowledge_base_permission_record>(entity =>
        {
            entity.ToTable("knowledge_base_permission_record");
            entity.HasKey(value => new { value.KnowledgeBaseId, value.PrincipalType, value.PrincipalId });
            entity.Property(value => value.PrincipalType).HasConversion<string>().HasMaxLength(16);
            entity.HasIndex(value => new { value.PrincipalType, value.PrincipalId });
        });

        builder.Entity<document_record>(entity =>
        {
            entity.ToTable("document_record");
            entity.Property(value => value.FileName).HasMaxLength(500);
            entity.Property(value => value.StoragePath).HasMaxLength(1000);
            entity.Property(value => value.MimeType).HasColumnName("mime_type").HasMaxLength(128);
            entity.Property(value => value.Status).HasConversion<string>().HasMaxLength(32);
            entity.HasOne<knowledge_base_record>().WithMany().HasForeignKey(value => value.KnowledgeBaseId);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(value => value.CreatedBy).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(value => new { value.KnowledgeBaseId, value.Status });
        });

        builder.Entity<document_permission_record>(entity =>
        {
            entity.ToTable("document_permission_record");
            entity.HasKey(value => new { value.DocumentId, value.PrincipalType, value.PrincipalId });
            entity.Property(value => value.PrincipalType).HasConversion<string>().HasMaxLength(16);
            entity.HasOne<document_record>().WithMany().HasForeignKey(value => value.DocumentId);
            entity.HasIndex(value => new { value.PrincipalType, value.PrincipalId });
        });

        builder.Entity<document_chunk_record>(entity =>
        {
            entity.ToTable("document_chunk_record");
            entity.HasOne<document_record>().WithMany().HasForeignKey(value => value.DocumentId);
            entity.HasIndex(value => new { value.DocumentId, value.Position }).IsUnique();
            entity.HasIndex(value => value.SearchVector).HasMethod("GIN");
        });
    }

    private static void ConfigureConversations(ModelBuilder builder)
    {
        builder.Entity<conversation_record>(entity =>
        {
            entity.ToTable("conversation_record");
            entity.Property(value => value.Title).HasMaxLength(300);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(value => value.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<message_record>(entity =>
        {
            entity.ToTable("message_record");
            entity.Property(value => value.Role).HasMaxLength(32);
            entity.HasOne<conversation_record>().WithMany().HasForeignKey(value => value.ConversationId);
            entity.HasIndex(value => new { value.ConversationId, value.CreatedAt });
        });
    }

    private static void ConfigureAudit(ModelBuilder builder)
    {
        builder.Entity<audit_log_record>(entity =>
        {
            entity.ToTable("audit_log_record");
            entity.Property(value => value.IpAddress).HasMaxLength(64);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(value => value.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(value => value.CreatedAt);
        });
    }

    private static void ConfigureRefreshTokens(ModelBuilder builder)
    {
        builder.Entity<refresh_token_record>(entity =>
        {
            entity.ToTable("refresh_token_record");
            entity.Property(value => value.TokenHash).HasMaxLength(64);
            entity.Property(value => value.SecurityStamp).HasMaxLength(64);
            entity.HasIndex(value => value.TokenHash).IsUnique();
            entity.HasIndex(value => new { value.UserId, value.RevokedAt });
            entity.HasOne<AppUser>().WithMany().HasForeignKey(value => value.UserId);
        });
    }

    private static void ConfigureMenuConfigs(ModelBuilder builder)
    {
        builder.Entity<menu_config_record>(entity =>
        {
            entity.ToTable("menu_config_record");
            entity.Property(value => value.Key).HasMaxLength(100);
            entity.Property(value => value.Label).HasMaxLength(100);
            entity.Property(value => value.Icon).HasMaxLength(100);
            var rolesBuilder = entity.Property(value => value.Roles)
                .HasConversion(
                    v => string.Join(",", v ?? Array.Empty<string>()),
                    v => (v ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
                .HasMaxLength(500);
            rolesBuilder.Metadata.SetValueComparer(new ArrayValueComparer<string>());
            entity.HasIndex(value => value.Key).IsUnique();
            entity.HasOne<menu_config_record>().WithMany().HasForeignKey(value => value.ParentId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureApiLogs(ModelBuilder builder)
    {
        builder.Entity<api_log_record>(entity =>
        {
            entity.ToTable("api_log");
            entity.Property(value => value.Id).HasColumnName("id");
            entity.Property(value => value.Timestamp).HasColumnName("timestamp");
            entity.Property(value => value.Level).HasColumnName("level").HasMaxLength(16);
            entity.Property(value => value.RequestId).HasColumnName("request_id").HasMaxLength(64);
            entity.Property(value => value.SourceContext).HasColumnName("source_context").HasMaxLength(500);
            entity.Property(value => value.RequestPath).HasColumnName("request_path").HasMaxLength(500);
            entity.Property(value => value.Message).HasColumnName("message");
            entity.Property(value => value.Exception).HasColumnName("exception");
            entity.Property(value => value.ElapsedMs).HasColumnName("elapsed_ms");
            // 访问维度列（kind='access' 行专用，消息行为 NULL）与 022 迁移脚本对应。
            entity.Property(value => value.Kind).HasColumnName("kind").HasMaxLength(16).IsRequired();
            entity.Property(value => value.UserName).HasColumnName("user_name").HasMaxLength(256);
            entity.Property(value => value.Action).HasColumnName("action").HasMaxLength(500);
            entity.Property(value => value.StatusCode).HasColumnName("status_code");
            entity.Property(value => value.RequestBody).HasColumnName("request_body");
            entity.Property(value => value.ResponseBody).HasColumnName("response_body");
            entity.HasIndex(value => value.RequestId);
            entity.HasIndex(value => value.Timestamp);
            entity.HasIndex(value => new { value.Kind, value.Timestamp }).IsDescending();
        });
    }

    private static void ConfigureOperationAudits(ModelBuilder builder)
    {
        builder.Entity<operation_audit_record>(entity =>
        {
            entity.ToTable("operation_audit_record");
            entity.Property(value => value.ActorName).HasMaxLength(64);
            entity.Property(value => value.Action).HasMaxLength(64);
            entity.Property(value => value.TargetType).HasMaxLength(64);
            entity.Property(value => value.TargetId).HasMaxLength(128);
            entity.Property(value => value.Summary).HasMaxLength(500);
            entity.HasIndex(value => value.CreatedAt).IsDescending();
            entity.HasIndex(value => value.ActorId);
            entity.HasIndex(value => value.Action);
        });
    }

    private static void ConfigureRoleEndpointGrants(ModelBuilder builder)
    {
        builder.Entity<role_endpoint_grant_record>(entity =>
        {
            entity.ToTable("role_endpoint_grant");
            entity.HasKey(value => new { value.RoleName, value.EndpointKey });
            entity.Property(value => value.RoleName).HasMaxLength(256);
            entity.Property(value => value.MenuKey).HasMaxLength(100);
            entity.Property(value => value.EndpointKey).HasMaxLength(200);
            entity.HasIndex(value => value.MenuKey);
            entity.HasIndex(value => value.EndpointKey);
        });
    }

    // 5 个默认角色 seed 进 AspNetRoles：bootstrap 阶段由 AdminBootstrapper 保证存在；任何角色后续可重命名或删除。
    private static void SeedRoles(ModelBuilder builder)
    {
        var roles = new[]
        {
            Role("8f86fa4c-c8e5-4bc0-a563-528b70a74576", "Admin"),
            Role("1f0187a1-2897-4bf8-ab73-27fa1a2e4c76", "KbManager"),
            Role("cb81efa4-2203-4f2b-88e4-1e18af3ba95c", "Editor"),
            Role("eadf72f7-d444-43cc-9fd9-5aa320640274", "Viewer"),
            Role("9081bc7b-1762-43cf-8649-bc30ed21ec52", "Auditor")
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
