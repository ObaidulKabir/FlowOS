using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace FlowOS.Infrastructure.Services;

public class ConfigurationPublisher : IConfigurationPublisher
{
    private readonly FlowOSDbContext _context;
    private readonly ILogger<ConfigurationLoader> _logger;

    public ConfigurationPublisher(FlowOSDbContext context, ILogger<ConfigurationLoader> logger)
    {
        _context = context;
        _logger = logger;
    }

    public string? ResolveConfigRoot(IEnumerable<string>? additionalCandidates = null)
    {
        var candidates = new List<string>
        {
            Path.Combine(Directory.GetCurrentDirectory(), "flowos-config"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "flowos-config"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "flowos-config")
        };

        if (additionalCandidates != null)
            candidates.AddRange(additionalCandidates);

        return candidates.FirstOrDefault(Directory.Exists);
    }

    public async Task PublishAsync(Guid tenantId, string configRoot, CancellationToken cancellationToken = default)
    {
        var loader = new ConfigurationLoader(_context, _logger, configRoot);
        // ConfigurationLoader currently has no CancellationToken overload
        await loader.LoadAllAsync(tenantId);
    }
}
