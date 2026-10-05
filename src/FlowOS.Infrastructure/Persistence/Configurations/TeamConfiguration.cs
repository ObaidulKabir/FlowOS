using System.Text.Json;
using FlowOS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowOS.Infrastructure.Persistence.Configurations;

public class TeamConfiguration : IEntityTypeConfiguration<Team>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public void Configure(EntityTypeBuilder<Team> builder)
    {
        builder.HasKey(t => t.Id);
        
        builder.HasIndex(t => new { t.TenantId, t.Name }).IsUnique();

        builder.Property(t => t.Name).IsRequired().HasMaxLength(200);
        builder.Property(t => t.Description).HasMaxLength(1000);

        // Store Capabilities as JSON
        builder.Property(t => t.Capabilities)
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => JsonSerializer.Deserialize<List<string>>(v, JsonOptions) ?? new List<string>()
            );

        // Map hierarchy levels as owned collection stored as JSON in the same table
        builder.OwnsMany(t => t.Hierarchy, h =>
        {
            h.ToJson();
            h.Property(l => l.Name).IsRequired();
            h.Property(l => l.Order).IsRequired();
        });

        builder.HasMany(t => t.Members)
            .WithOne()
            .HasForeignKey(m => m.TeamId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class TeamMemberConfiguration : IEntityTypeConfiguration<TeamMember>
{
    public void Configure(EntityTypeBuilder<TeamMember> builder)
    {
        builder.HasKey(m => m.Id);
        
        // A user can only be in a team once
        builder.HasIndex(m => new { m.TeamId, m.TenantUserId }).IsUnique();

        builder.Property(m => m.HierarchyLevelName).IsRequired().HasMaxLength(100);
        
        builder.HasOne<TenantUser>()
            .WithMany()
            .HasForeignKey(m => m.TenantUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
