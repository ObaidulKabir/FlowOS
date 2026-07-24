using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Commands.Admin;
using FlowOS.Application.Common.Interfaces;
using MediatR;

namespace FlowOS.Application.Handlers.Admin;

public class PublishConfigurationCommandHandler
    : IRequestHandler<PublishConfigurationCommand, PublishConfigurationResult>
{
    private readonly IConfigurationPublisher _publisher;

    public PublishConfigurationCommandHandler(IConfigurationPublisher publisher)
    {
        _publisher = publisher;
    }

    public async Task<PublishConfigurationResult> Handle(
        PublishConfigurationCommand request,
        CancellationToken cancellationToken)
    {
        var configRoot = _publisher.ResolveConfigRoot();
        if (configRoot == null)
        {
            return new PublishConfigurationResult(
                false,
                "Configuration directory not found among known candidate paths.",
                null);
        }

        await _publisher.PublishAsync(request.TenantId, configRoot, cancellationToken);
        return new PublishConfigurationResult(
            true,
            "Configuration published successfully.",
            configRoot);
    }
}
