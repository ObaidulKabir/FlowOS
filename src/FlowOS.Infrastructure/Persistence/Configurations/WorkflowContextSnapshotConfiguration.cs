using System.Text.Json;
using FlowOS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowOS.Infrastructure.Persistence.Configurations;

public class WorkflowContextSnapshotConfiguration : IEntityTypeConfiguration<WorkflowContextSnapshot>
{
    private static readonly JsonSerializerOptions JsonOptions = new();

    public void Configure(EntityTypeBuilder<WorkflowContextSnapshot> builder)
    {
        builder.ToTable("WorkflowContextSnapshots");
        builder.HasKey(x => x.WorkflowInstanceId);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.ContextBindingRevisionId).IsRequired();
        builder.Property(x => x.SourceSystem).HasMaxLength(150).IsRequired(false);
        builder.Property(x => x.ExternalEntityId).HasMaxLength(300).IsRequired(false);
        builder.Property(x => x.ConcurrencyVersion).IsConcurrencyToken();

        var canonicalComparer = new ValueComparer<Dictionary<string, JsonElement>>(
            (left, right) => JsonSerializer.Serialize(left, JsonOptions) == JsonSerializer.Serialize(right, JsonOptions),
            value => JsonSerializer.Serialize(value, JsonOptions).GetHashCode(),
            value => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                JsonSerializer.Serialize(value, JsonOptions), JsonOptions)
                ?? new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase));

        builder.Property(x => x.CanonicalData)
            .HasConversion(
                value => JsonSerializer.Serialize(value, JsonOptions),
                value => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(value, JsonOptions)
                    ?? new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase))
            .Metadata.SetValueComparer(canonicalComparer);

        var metadataComparer = new ValueComparer<Dictionary<string, string>>(
            (left, right) => JsonSerializer.Serialize(left, JsonOptions) == JsonSerializer.Serialize(right, JsonOptions),
            value => JsonSerializer.Serialize(value, JsonOptions).GetHashCode(),
            value => JsonSerializer.Deserialize<Dictionary<string, string>>(
                JsonSerializer.Serialize(value, JsonOptions), JsonOptions)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        builder.Property(x => x.BusinessMetadata)
            .HasConversion(
                value => JsonSerializer.Serialize(value, JsonOptions),
                value => JsonSerializer.Deserialize<Dictionary<string, string>>(value, JsonOptions)
                    ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
            .Metadata.SetValueComparer(metadataComparer);

        builder.HasOne<FlowOS.Workflows.Domain.WorkflowInstance>()
            .WithOne()
            .HasForeignKey<WorkflowContextSnapshot>(x => new { x.WorkflowInstanceId, x.TenantId })
            .HasPrincipalKey<FlowOS.Workflows.Domain.WorkflowInstance>(x => new { x.Id, x.TenantId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<WorkflowContextBindingRevision>()
            .WithMany()
            .HasForeignKey(x => x.ContextBindingRevisionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.TenantId, x.ContextBindingRevisionId });
        builder.HasIndex(x => new { x.TenantId, x.SourceSystem, x.ExternalEntityId });
    }
}
