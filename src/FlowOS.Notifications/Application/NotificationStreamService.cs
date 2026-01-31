using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Notifications.Domain;

namespace FlowOS.Notifications.Application;

public class NotificationStreamService
{
    private readonly ConcurrentDictionary<Guid, List<StreamClient>> _clients = new();

    public void AddClient(Guid tenantId, StreamClient client)
    {
        var tenantClients = _clients.GetOrAdd(tenantId, _ => new List<StreamClient>());
        lock (tenantClients)
        {
            tenantClients.Add(client);
        }
    }

    public void RemoveClient(Guid tenantId, StreamClient client)
    {
        if (_clients.TryGetValue(tenantId, out var tenantClients))
        {
            lock (tenantClients)
            {
                tenantClients.Remove(client);
            }
        }
    }

    public async Task BroadcastAsync(Notification notification)
    {
        if (_clients.TryGetValue(notification.TenantId, out var tenantClients))
        {
            List<StreamClient> clientsSnapshot;
            lock (tenantClients)
            {
                clientsSnapshot = tenantClients.ToList();
            }

            foreach (var client in clientsSnapshot)
            {
                try
                {
                    // Format as SSE data: "data: {json}\n\n"
                    var json = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        notification.Message,
                        notification.Severity,
                        notification.CreatedAt,
                        notification.EventType
                    });
                    
                    await client.Writer.WriteAsync($"data: {json}\n\n");
                    await client.Writer.FlushAsync();
                }
                catch
                {
                    // Client disconnected
                    RemoveClient(notification.TenantId, client);
                }
            }
        }
    }
}

public class StreamClient
{
    public Guid Id { get; } = Guid.NewGuid();
    public System.IO.TextWriter Writer { get; }

    public StreamClient(System.IO.TextWriter writer)
    {
        Writer = writer;
    }
}
