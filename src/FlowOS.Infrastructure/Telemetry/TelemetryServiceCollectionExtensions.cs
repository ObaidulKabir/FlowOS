using System;
using FlowOS.Core.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace FlowOS.Infrastructure.Telemetry;

/// <summary>
/// Registers the OpenTelemetry SDK for a FlowOS host (CR-07).
///
/// Telemetry is exported only when an OTLP endpoint is configured with the standard
/// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> environment variable (for example
/// <c>http://otel-collector:4317</c>). Without it nothing is registered, so there is no overhead.
/// <c>OTEL_SERVICE_NAME</c>, <c>OTEL_RESOURCE_ATTRIBUTES</c> and the other standard
/// <c>OTEL_*</c> variables are honoured by the SDK.
/// </summary>
public static class TelemetryServiceCollectionExtensions
{
    public const string OtlpEndpointVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";

    public static bool IsExportConfigured() =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(OtlpEndpointVariable));

    public static IServiceCollection AddFlowOsTelemetry(this IServiceCollection services, string serviceName)
    {
        if (!IsExportConfigured())
            return services;

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName,
                serviceVersion: typeof(TelemetryServiceCollectionExtensions).Assembly.GetName().Version?.ToString()))
            .WithTracing(tracing => tracing
                .AddSource(FlowOsTelemetry.WorkflowSourceName, FlowOsTelemetry.BackgroundSourceName)
                // Npgsql emits its own ActivitySource named "Npgsql" (database spans).
                .AddSource("Npgsql")
                .AddAspNetCoreInstrumentation(options =>
                {
                    // Health probes would flood traces without adding information.
                    options.Filter = context =>
                        !context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase);
                    // Query strings can carry API keys (?apiKey=) and tenant IDs; never record them.
                    options.EnrichWithHttpRequest = (activity, _) => activity.SetTag("url.query", null);
                })
                .AddHttpClientInstrumentation()
                .AddOtlpExporter())
            .WithMetrics(metrics => metrics
                .AddMeter(FlowOsTelemetry.MeterName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddOtlpExporter());

        return services;
    }
}
