using FlowOS.Infrastructure.Persistence.ReadModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowOS.Infrastructure.Persistence.Configurations;

public class AgentInsightReadModelConfiguration : IEntityTypeConfiguration<AgentInsightReadModel>
{
    public void Configure(EntityTypeBuilder<AgentInsightReadModel> builder)
    {
        builder.HasKey(x => x.Id);
        
        builder.Property(x => x.AgentId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.Insight)
            .IsRequired();

        builder.Property(x => x.ContextObjective)
            .HasMaxLength(200);

        // Index for fast lookup by WorkflowInstanceId
        builder.HasIndex(x => x.WorkflowInstanceId);
    }
}
