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

            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                notification.Id,
                notification.Message,
                notification.Severity,
                notification.CreatedAt,
                notification.EventType
            });
            
            var message = $"data: {json}\n\n";

            var tasks = clientsSnapshot
                .Where(c => notification.TargetUserId == null || c.UserId == notification.TargetUserId)
                .Select(async client => 
            {
                try
                {
                    await client.WriteMessageAsync(message);
                }
                catch
                {
                    // Client disconnected
                    RemoveClient(notification.TenantId, client);
                }
            });

            await Task.WhenAll(tasks);
        }
    }
}

public class StreamClient : IDisposable
{
    public Guid Id { get; } = Guid.NewGuid();
    public Guid UserId { get; }
    private readonly System.IO.TextWriter _writer;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public StreamClient(System.IO.TextWriter writer, Guid userId)
    {
        _writer = writer;
        UserId = userId;
    }

    public async Task WriteMessageAsync(string message)
    {
        await _semaphore.WaitAsync();
        try
        {
            await _writer.WriteAsync(message);
            await _writer.FlushAsync();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose()
    {
        _semaphore.Dispose();
    }
}
