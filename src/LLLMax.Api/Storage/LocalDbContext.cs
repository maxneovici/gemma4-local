using Microsoft.EntityFrameworkCore;

namespace LLLMax.Api.Storage;

public sealed class LocalDbContext(DbContextOptions<LocalDbContext> options) : DbContext(options)
{
    public DbSet<SessionEntity> Sessions => Set<SessionEntity>();

    public DbSet<SessionMessageEntity> SessionMessages => Set<SessionMessageEntity>();

    public DbSet<AppMetadataEntity> AppMetadata => Set<AppMetadataEntity>();

    public DbSet<BackgroundJobEntity> BackgroundJobs => Set<BackgroundJobEntity>();

    public DbSet<ApprovalEntity> Approvals => Set<ApprovalEntity>();

    public DbSet<TaskGraphEntity> TaskGraphs => Set<TaskGraphEntity>();

    public DbSet<MemoryConsolidationJobEntity> MemoryConsolidationJobs => Set<MemoryConsolidationJobEntity>();

    public DbSet<ApiIntegrationEntity> ApiIntegrations => Set<ApiIntegrationEntity>();

    public DbSet<McpServerEntity> McpServers => Set<McpServerEntity>();

    public DbSet<McpToolEntity> McpTools => Set<McpToolEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SessionEntity>(entity =>
        {
            entity.ToTable("sessions");
            entity.HasKey(session => session.Id);
            entity.Property(session => session.Title).IsRequired();
            entity.Property(session => session.Agent).IsRequired();
            entity.HasMany(session => session.Messages)
                .WithOne(message => message.Session)
                .HasForeignKey(message => message.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SessionMessageEntity>(entity =>
        {
            entity.ToTable("session_messages");
            entity.HasKey(message => new { message.SessionId, message.Ordinal });
            entity.Property(message => message.Role).IsRequired();
            entity.Property(message => message.Content).IsRequired();
        });

        modelBuilder.Entity<AppMetadataEntity>(entity =>
        {
            entity.ToTable("app_metadata");
            entity.HasKey(metadata => metadata.Key);
            entity.Property(metadata => metadata.Value).IsRequired();
        });

        modelBuilder.Entity<BackgroundJobEntity>(entity =>
        {
            entity.ToTable("background_jobs");
            entity.HasKey(job => job.Id);
            entity.Property(job => job.Kind).IsRequired();
            entity.Property(job => job.Status).IsRequired();
            entity.Property(job => job.PayloadJson).IsRequired();
        });

        modelBuilder.Entity<ApprovalEntity>(entity =>
        {
            entity.ToTable("approvals");
            entity.HasKey(approval => approval.Id);
            entity.Property(approval => approval.Kind).IsRequired();
            entity.Property(approval => approval.Status).IsRequired();
            entity.Property(approval => approval.Title).IsRequired();
            entity.Property(approval => approval.Description).IsRequired();
            entity.Property(approval => approval.PayloadJson).IsRequired();
        });

        modelBuilder.Entity<TaskGraphEntity>(entity =>
        {
            entity.ToTable("task_graphs");
            entity.HasKey(graph => graph.Id);
            entity.Property(graph => graph.SessionId).IsRequired();
            entity.Property(graph => graph.Goal).IsRequired();
            entity.Property(graph => graph.Status).IsRequired();
            entity.Property(graph => graph.GraphJson).IsRequired();
        });

        modelBuilder.Entity<MemoryConsolidationJobEntity>(entity =>
        {
            entity.ToTable("memory_consolidation_jobs");
            entity.HasKey(job => job.Id);
            entity.Property(job => job.SessionId).IsRequired();
            entity.Property(job => job.Status).IsRequired();
        });

        modelBuilder.Entity<ApiIntegrationEntity>(entity =>
        {
            entity.ToTable("api_integrations");
            entity.HasKey(integration => integration.Id);
            entity.Property(integration => integration.Name).IsRequired();
            entity.Property(integration => integration.BaseUrl).IsRequired();
            entity.Property(integration => integration.OperationsJson).IsRequired();
        });

        modelBuilder.Entity<McpServerEntity>(entity =>
        {
            entity.ToTable("mcp_servers");
            entity.HasKey(server => server.Id);
            entity.Property(server => server.Name).IsRequired();
            entity.Property(server => server.Transport).IsRequired();
            entity.Property(server => server.Endpoint).IsRequired();
            entity.Property(server => server.Status).IsRequired();
            entity.HasMany(server => server.Tools)
                .WithOne(tool => tool.Server)
                .HasForeignKey(tool => tool.ServerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<McpToolEntity>(entity =>
        {
            entity.ToTable("mcp_tools");
            entity.HasKey(tool => new { tool.ServerId, tool.Name });
            entity.Property(tool => tool.Description).IsRequired();
            entity.Property(tool => tool.ArgumentsJsonSchema).IsRequired();
            entity.Property(tool => tool.ApprovalStatus).IsRequired();
        });

        ApplySnakeCaseNames(modelBuilder);
    }

    private static void ApplySnakeCaseNames(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }
        }
    }

    private static string ToSnakeCase(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var builder = new System.Text.StringBuilder(value.Length + 8);

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];

            if (char.IsUpper(character))
            {
                if (index > 0)
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(character));
            }
            else
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }
}
