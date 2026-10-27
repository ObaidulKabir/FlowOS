# 28. Observability (OpenTelemetry)

FlowOS emits OpenTelemetry traces and metrics from the API host (`flowos-api`) and the MCP host (`flowos-mcp`). Telemetry is for operators: it shows where time goes and where processing fails. It never carries personal data.

## Turning it on

Telemetry is exported only when the standard OTLP variable is set on a host:

```bash
OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317
```

Without it, nothing is registered and there is no overhead. The other standard `OTEL_*` variables also work, for example `OTEL_SERVICE_NAME`, `OTEL_RESOURCE_ATTRIBUTES=deployment.environment=staging`, `OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf`, and `OTEL_TRACES_SAMPLER=parentbased_traceidratio` with `OTEL_TRACES_SAMPLER_ARG=0.1`.

### Local collector for development

```bash
docker run --rm -p 4317:4317 -p 4318:4318 -p 16686:16686 jaegertracing/all-in-one:latest
export OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
dotnet run --project src/FlowOS.Api
```

Open http://localhost:16686, choose the service `flowos-api`, and start a workflow: the trace shows the HTTP request, the `workflow.advance` span, the PostgreSQL calls, and later the `outbox.process` span for the events it wrote.

## Traces

| Span | Source | When | Main attributes |
|------|--------|------|-----------------|
| HTTP server and client spans | ASP.NET Core, HttpClient | Every request (except `/health*`) | method, route, status. Query strings are not recorded because they can carry API keys. |
| Database spans | `Npgsql` | Every PostgreSQL command | statement type and duration |
| `workflow.advance` | `FlowOS.Workflows.Engine` | Every workflow advance (transition attempt) | `workflow.instanceId`, `workflow.definitionId`, `workflow.event`, `flowos.tenant.hash`, `flowos.correlation_id`, `flowos.causation_id` (event ID), `outcome` |
| `outbox.process` | `FlowOS.Infrastructure.Background` | Each outbox message the background worker handles | `outbox.messageId`, `outbox.type`, `outbox.retryCount`, `flowos.tenant.hash` |
| `workflow.timer.process` | `FlowOS.Infrastructure.Background` | Each timer-processor pass | `timer.executed` (number of timers fired) |

**One trace from request to consumer.** When an outbox message is written, its `TraceParent` column stores the W3C trace context of the request that wrote it. The `outbox.process` span starts with that context as its parent, so the trace runs from the API request through the command handler, the database, and the outbox to the consumer. Messages written before this change have no stored context and start a new trace.

## Metrics (meter `FlowOS`)

| Metric | Type | Unit | Attributes | Use |
|--------|------|------|------------|-----|
| `flowos.workflow.transition.duration` | histogram | ms | `outcome` | Transition latency (p50/p95/p99) |
| `flowos.workflow.transitions` | counter | {transition} | `outcome` = succeeded, failed | Throughput and failure rate |
| `flowos.outbox.messages` | counter | {message} | `outcome` = processed, failed, dead_letter | Processor throughput; dead letters |
| `flowos.outbox.lag` | histogram | ms | | Time from write to processing (target p95 < 5 s, CR-13) |
| `flowos.timer.lag` | histogram | ms | | Time from a timer's due time to execution |

ASP.NET Core and HttpClient metrics (request duration, active requests) are exported as well.

## Privacy rules

- Tenants appear only as `flowos.tenant.hash`: the first 16 hex characters of SHA-256 of the tenant ID. Operators can group by tenant without exporting the tenant ID.
- No user names, emails, API keys, payloads, business context, or AI prompts are recorded in spans or metrics.
- Query strings are removed from HTTP spans.
- Do not expose telemetry data to tenants.

## Changing telemetry

Spans, attributes and metrics are defined in one place: `src/FlowOS.Core/Telemetry/FlowOsTelemetry.cs`. SDK registration lives in `src/FlowOS.Infrastructure/Telemetry/TelemetryServiceCollectionExtensions.cs`. A new attribute must not carry personal data; add a test in `tests/FlowOS.UnitTests/Telemetry/` and update this chapter and the MCP guidance ("Observability law").
