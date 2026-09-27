using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Core.Common.Models;
using FlowOS.Domain;
using FlowOS.Domain.Entities;
using FlowOS.Events.Models;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Workflows.Domain;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.Infrastructure.Services;

public sealed class TenantBackupService : ITenantBackupService
{
    public const string Format = "flowos-tenant-backup";
    public const int FormatVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly FlowOSDbContext _db;

    public TenantBackupService(FlowOSDbContext db)
    {
        _db = db;
    }

    public async Task<string> ExportJsonAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken)
            ?? throw new TenantBackupException("BACKUP-NOT-FOUND", $"Tenant {tenantId} was not found.", 404);

        var classes = await _db.WorkflowClasses.AsNoTracking().Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken);
        var bindings = await _db.WorkflowContextBindings.AsNoTracking().Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken);
        var bindingIds = bindings.Select(x => x.Id).ToList();
        var revisions = await _db.WorkflowContextBindingRevisions.AsNoTracking()
            .Where(x => bindingIds.Contains(x.BindingId))
            .ToListAsync(cancellationToken);
        var plugins = await _db.PluginBindings.AsNoTracking().Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken);
        var capabilities = await _db.CapabilityBindings.AsNoTracking().Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken);
        var instances = await _db.WorkflowInstances.AsNoTracking().Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken);
        var definitions = await _db.WorkflowDefinitions.AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .ToListAsync(cancellationToken);
        var machines = await _db.StateMachineDefinitions.AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .ToListAsync(cancellationToken);
        var snapshots = await _db.WorkflowContextSnapshots.AsNoTracking().Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken);
        var events = await _db.Events.AsNoTracking().Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken);
        var actionLogs = await _db.ActionExecutionLogs.AsNoTracking().Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken);
        var timers = await _db.WorkflowTimerJobs.AsNoTracking().Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken);

        var bundle = new JsonObject
        {
            ["format"] = Format,
            ["formatVersion"] = FormatVersion,
            ["flowOsVersion"] = FlowOsRelease.Version,
            ["takenAtUtc"] = DateTime.UtcNow,
            ["sourceTenantId"] = tenantId,
            ["sourceTenantName"] = tenant.Name,
            ["policy"] = "Point-in-time Designed App and running-instance snapshot. Pending timers are restored as already processed so the destination does not repeat side effects. Outbox deliveries, API keys, and user passwords are not included.",
            ["workflowClasses"] = WriteAll(classes),
            ["stateMachines"] = WriteAll(machines),
            ["workflowDefinitions"] = WriteAll(definitions),
            ["contextBindings"] = WriteAll(bindings),
            ["contextBindingRevisions"] = WriteAll(revisions),
            ["pluginBindings"] = WriteAll(plugins),
            ["capabilityBindings"] = WriteAll(capabilities),
            ["workflowInstances"] = WriteAll(instances),
            ["contextSnapshots"] = WriteAll(snapshots),
            ["events"] = WriteAll(events),
            ["actionLogs"] = WriteAll(actionLogs),
            ["timerJobs"] = WriteAll(timers)
        };

        return bundle.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public async Task<TenantBackupRestoreResult> RestoreJsonAsync(
        Guid destinationTenantId,
        string json,
        CancellationToken cancellationToken = default)
    {
        if (destinationTenantId == Guid.Empty)
            throw new TenantBackupException("BACKUP-TENANT", "A destination tenant is required.", 400);

        var destination = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == destinationTenantId, cancellationToken)
            ?? throw new TenantBackupException("BACKUP-NOT-FOUND", $"Destination tenant {destinationTenantId} was not found.", 404);

        JsonObject bundle;
        try
        {
            bundle = JsonNode.Parse(json) as JsonObject
                ?? throw new TenantBackupException("BACKUP-FORMAT", "Backup file is not a JSON object.", 400);
        }
        catch (JsonException ex)
        {
            throw new TenantBackupException("BACKUP-FORMAT", "Backup file is not valid JSON.", 400, ex);
        }

        if (bundle["format"]?.ToString() != Format || bundle["formatVersion"]?.GetValue<int>() != FormatVersion)
            throw new TenantBackupException("BACKUP-FORMAT", "Backup file is not a FlowOS tenant backup of format version 1.", 400);

        var flowOsVersion = bundle["flowOsVersion"]?.ToString() ?? string.Empty;
        var compatibility = AssessBundle(bundle, flowOsVersion);
        if (compatibility is FlowOsCompatibility.NewerThanHost or FlowOsCompatibility.IncompatibleMajor or FlowOsCompatibility.Unreadable)
        {
            throw new TenantBackupException(
                "BACKUP-VERSION",
                $"This site is FlowOS {FlowOsRelease.Version}. The backup depends on {flowOsVersion} ({compatibility}). Restore it on a host with the same major version that is not older than the backup.",
                409);
        }

        var sourceTenantId = bundle["sourceTenantId"]!.GetValue<Guid>();
        var takenAt = bundle["takenAtUtc"]!.GetValue<DateTime>();

        var machines = ReadAll<StateMachineDefinition>(bundle["stateMachines"] as JsonArray);
        var classes = ReadAll<WorkflowClass>(bundle["workflowClasses"] as JsonArray);
        var definitions = ReadAll<WorkflowDefinition>(bundle["workflowDefinitions"] as JsonArray);
        var bindings = ReadAll<WorkflowContextBinding>(bundle["contextBindings"] as JsonArray);
        var revisions = ReadAll<WorkflowContextBindingRevision>(bundle["contextBindingRevisions"] as JsonArray);
        var plugins = ReadAll<PluginBindingRecord>(bundle["pluginBindings"] as JsonArray);
        var capabilities = ReadAll<CapabilityBindingRecord>(bundle["capabilityBindings"] as JsonArray);
        var instances = ReadAll<WorkflowInstance>(bundle["workflowInstances"] as JsonArray);
        var snapshots = ReadAll<WorkflowContextSnapshot>(bundle["contextSnapshots"] as JsonArray);
        var events = ReadEvents(bundle["events"] as JsonArray);
        var actionLogs = ReadAll<WorkflowActionExecutionLog>(bundle["actionLogs"] as JsonArray);
        var timers = ReadAll<WorkflowTimerJob>(bundle["timerJobs"] as JsonArray);

        Retarget(machines, destinationTenantId);
        Retarget(classes, destinationTenantId);
        Retarget(definitions, destinationTenantId);
        Retarget(bindings, destinationTenantId);
        Retarget(plugins, destinationTenantId);
        Retarget(capabilities, destinationTenantId);
        Retarget(instances, destinationTenantId);
        Retarget(snapshots, destinationTenantId);
        Retarget(events, destinationTenantId);
        Retarget(actionLogs, destinationTenantId);
        foreach (var timer in timers)
        {
            Retarget(timer, destinationTenantId);
            if (!timer.IsProcessed)
                timer.MarkAsProcessed();
        }

        await EnsureIdsFreeAsync(classes.Select(x => x.Id), _db.WorkflowClasses.Select(x => x.Id), "workflow class", cancellationToken);
        await EnsureIdsFreeAsync(bindings.Select(x => x.Id), _db.WorkflowContextBindings.Select(x => x.Id), "business context", cancellationToken);
        await EnsureIdsFreeAsync(instances.Select(x => x.Id), _db.WorkflowInstances.Select(x => x.Id), "workflow instance", cancellationToken);
        await EnsureIdsFreeAsync(events.Select(x => x.EventId), _db.Events.Select(x => x.EventId), "event", cancellationToken);

        _db.StateMachineDefinitions.AddRange(machines);
        _db.WorkflowClasses.AddRange(classes);
        _db.WorkflowDefinitions.AddRange(definitions);
        _db.WorkflowContextBindings.AddRange(bindings);
        _db.WorkflowContextBindingRevisions.AddRange(revisions);
        _db.PluginBindings.AddRange(plugins);
        _db.CapabilityBindings.AddRange(capabilities);
        _db.WorkflowInstances.AddRange(instances);
        _db.WorkflowContextSnapshots.AddRange(snapshots);
        _db.Events.AddRange(events);
        _db.ActionExecutionLogs.AddRange(actionLogs);
        _db.WorkflowTimerJobs.AddRange(timers);
        PreserveKeys();

        await _db.SaveChangesAsync(cancellationToken);

        return new TenantBackupRestoreResult(
            destination.TenantId,
            sourceTenantId,
            takenAt,
            flowOsVersion,
            compatibility.ToString(),
            classes.Count,
            bindings.Count,
            plugins.Count,
            instances.Count,
            events.Count);
    }

    private void PreserveKeys()
    {
        foreach (var entry in _db.ChangeTracker.Entries())
        {
            var key = entry.Metadata.FindPrimaryKey();
            if (key == null) continue;
            foreach (var property in key.Properties)
            {
                var tracked = entry.Property(property.Name);
                if (tracked.CurrentValue is Guid guid && guid == Guid.Empty)
                    continue;
                if (tracked.CurrentValue is int number && number == 0)
                    continue;
                tracked.IsTemporary = false;
            }
        }
    }

    private static FlowOsCompatibility AssessBundle(JsonObject bundle, string hostStamp)
    {
        var verdict = FlowOsRelease.Assess(hostStamp);
        foreach (var section in new[]
        {
            "workflowClasses", "contextBindingRevisions", "pluginBindings"
        })
        {
            if (bundle[section] is not JsonArray rows) continue;
            foreach (var row in rows.OfType<JsonObject>())
            {
                var stamp = row["flowOsVersion"]?.ToString();
                if (string.IsNullOrWhiteSpace(stamp)) continue;
                var rowVerdict = FlowOsRelease.Assess(stamp);
                if (Rank(rowVerdict) > Rank(verdict))
                    verdict = rowVerdict;
            }
        }

        return verdict;
    }

    private static int Rank(FlowOsCompatibility compatibility) => compatibility switch
    {
        FlowOsCompatibility.Current => 0,
        FlowOsCompatibility.OlderCompatible => 1,
        FlowOsCompatibility.Unreadable => 2,
        FlowOsCompatibility.NewerThanHost => 3,
        FlowOsCompatibility.IncompatibleMajor => 4,
        _ => 2
    };

    private static async Task EnsureIdsFreeAsync(
        IEnumerable<Guid> incoming,
        IQueryable<Guid> existing,
        string label,
        CancellationToken cancellationToken)
    {
        var ids = incoming.Where(x => x != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0) return;
        var collision = await existing.Where(id => ids.Contains(id)).FirstOrDefaultAsync(cancellationToken);
        if (collision != Guid.Empty)
        {
            throw new TenantBackupException(
                "BACKUP-CONFLICT",
                $"A {label} from this backup already exists on this site ({collision}). Restore onto a site that does not already hold these records.",
                409);
        }
    }

    private static JsonArray WriteAll<T>(IEnumerable<T> entities) where T : class
    {
        var array = new JsonArray();
        foreach (var entity in entities)
            array.Add(Write(entity));
        return array;
    }

    private static JsonObject Write(object entity)
    {
        var node = new JsonObject { ["$type"] = entity.GetType().Name };
        foreach (var property in entity.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetMethod == null || property.GetIndexParameters().Length > 0 || !Include(property.PropertyType))
                continue;
            var value = property.GetValue(entity);
            node[JsonNamingPolicy.CamelCase.ConvertName(property.Name)] = value == null
                ? null
                : JsonSerializer.SerializeToNode(value, property.PropertyType, JsonOptions);
        }

        return node;
    }

    private static bool Include(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (underlying.IsPrimitive || underlying.IsEnum || underlying == typeof(string) || underlying == typeof(Guid)
            || underlying == typeof(DateTime) || underlying == typeof(DateTimeOffset) || underlying == typeof(decimal))
            return true;
        if (underlying == typeof(JsonElement))
            return true;
        if (typeof(System.Collections.IEnumerable).IsAssignableFrom(underlying))
            return true;
        var ns = underlying.Namespace ?? string.Empty;
        return ns.Contains("Blueprints", StringComparison.Ordinal) || ns.Contains("ValueObjects", StringComparison.Ordinal);
    }

    private static List<T> ReadAll<T>(JsonArray? rows) where T : class
    {
        var list = new List<T>();
        if (rows == null) return list;
        foreach (var row in rows.OfType<JsonObject>())
            list.Add(Read<T>(row));
        return list;
    }

    private static List<DomainEvent> ReadEvents(JsonArray? rows)
    {
        var list = new List<DomainEvent>();
        if (rows == null) return list;
        foreach (var row in rows.OfType<JsonObject>())
        {
            var typeName = row["$type"]?.ToString();
            DomainEvent? entity = typeName switch
            {
                nameof(TaskCompleted) => Read<TaskCompleted>(row),
                "AgentInsightGenerated" => Read<FlowOS.Agents.Events.AgentInsightGenerated>(row),
                _ => Read<StandardEvent>(row)
            };
            list.Add(entity);
        }

        return list;
    }

    private static T Read<T>(JsonObject row) where T : class
    {
        var entity = (T)(Activator.CreateInstance(typeof(T), nonPublic: true)
            ?? throw new TenantBackupException("BACKUP-FORMAT", $"Could not restore {typeof(T).Name}.", 400));
        foreach (var property in typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (FindSetter(property) == null || !Include(property.PropertyType))
                continue;
            var camel = JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            if (!row.TryGetPropertyValue(camel, out var node) || node == null)
                continue;
            var value = node.Deserialize(property.PropertyType, JsonOptions);
            SetProperty(entity, property, value);
        }

        return entity;
    }

    private static void Retarget<T>(IEnumerable<T> entities, Guid tenantId)
    {
        foreach (var entity in entities)
            Retarget(entity!, tenantId);
    }

    private static void Retarget(object entity, Guid tenantId)
    {
        var property = entity.GetType().GetProperty("TenantId", BindingFlags.Instance | BindingFlags.Public);
        if (property != null)
            SetProperty(entity, property, tenantId);
    }

    private static void SetProperty(object entity, PropertyInfo property, object? value)
    {
        FindSetter(property)?.Invoke(entity, new[] { value });
    }

    private static MethodInfo? FindSetter(PropertyInfo property)
    {
        var setter = property.GetSetMethod(true);
        if (setter != null)
            return setter;

        return property.DeclaringType?
            .GetProperty(property.Name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
            .GetSetMethod(true);
    }
}

public static class TenantBackupConfirmation
{
    public const string HeaderName = "X-FlowOS-Backup-Confirm";
    public const string Download = "download";
    public const string Restore = "restore";

    public static bool IsConfirmed(string? headerValue, string expected) =>
        string.Equals(headerValue?.Trim(), expected, StringComparison.Ordinal);
}

public sealed class TenantBackupException : Exception
{
    public string Code { get; }
    public int StatusCode { get; }

    public TenantBackupException(string code, string message, int statusCode, Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
        StatusCode = statusCode;
    }
}
