using System.Text.Json;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowOS.Infrastructure.Persistence.Configurations;

public class WorkflowClassConfiguration : IEntityTypeConfiguration<WorkflowClass>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public void Configure(EntityTypeBuilder<WorkflowClass> builder)
    {
        builder.HasKey(e => e.Id);
        
        builder.Property(e => e.Name).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Version).IsRequired().HasMaxLength(50);
        
        // Store Definition as JSON
        builder.Property(e => e.Definition)
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => JsonSerializer.Deserialize<WorkflowClassBlueprint>(v, JsonOptions) ?? new WorkflowClassBlueprint()
            );
    }
}
