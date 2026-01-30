using FlowOS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowOS.Infrastructure.Persistence.Configurations;

public class StateMachineDefinitionConfiguration : IEntityTypeConfiguration<StateMachineDefinition>
{
    public void Configure(EntityTypeBuilder<StateMachineDefinition> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.EntityType)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(s => s.Status)
            .HasConversion<string>();

        // Transitions handled as JSON or owned entity? 
        // For EF Core, let's treat them as Owned Entities or JSON column.
        // Assuming Postgres, JSON is best. For InMemory/General, Owned is safer.
        builder.OwnsMany(s => s.Transitions, t =>
        {
            t.WithOwner().HasForeignKey("StateMachineDefinitionId");
            t.Property<int>("Id"); // Shadow Key
            t.HasKey("Id");
            
            // Map Value Object properties
            t.Property(tr => tr.FromState).IsRequired();
            t.Property(tr => tr.ToState).IsRequired();
            t.Property(tr => tr.TriggerEventType);
            t.Property(tr => tr.EventId);
            
            // Ignore Constraints for now if they are complex dictionary
            t.Ignore(tr => tr.Constraints);
        });

        // States (HashSet<string>) - simplest is JSON conversion or separate table
        // For simplicity in Phase 1:
        builder.Property(s => s.States)
            .HasConversion(
                v => string.Join(',', v),
                v => new HashSet<string>(v.Split(',', StringSplitOptions.RemoveEmptyEntries)));
    }
}
