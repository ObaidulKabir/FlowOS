using System.Threading.Tasks;

namespace FlowOS.Core.Interfaces;

public interface IEventRegistry
{
    Task<bool> ExistsAsync(string eventId, Guid tenantId);
    Task ValidateAsync(string eventId, Guid tenantId); // Throws exception if invalid
}
