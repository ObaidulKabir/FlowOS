using System.Collections.Concurrent;
using FlowOS.Agents.Abstractions;

namespace FlowOS.Infrastructure.Services;

public sealed class LlmTransportOptions
{
    public const string ConfigurationSection = "FlowOS:Agents:LlmTransport";

    public int TimeoutSeconds { get; set; } = 30;
    public int MaxRetryAttempts { get; set; } = 2;
    public int BaseRetryDelayMilliseconds { get; set; } = 200;
    public int CircuitBreakerFailureThreshold { get; set; } = 5;
    public int CircuitBreakerBreakSeconds { get; set; } = 30;

    internal TimeSpan Timeout =>
        TimeSpan.FromSeconds(Math.Clamp(TimeoutSeconds, 1, 300));

    internal int RetryAttempts => Math.Clamp(MaxRetryAttempts, 0, 5);

    internal TimeSpan RetryDelay(int retryNumber)
    {
        var baseMilliseconds = Math.Clamp(BaseRetryDelayMilliseconds, 0, 30_000);
        var multiplier = 1 << Math.Clamp(retryNumber - 1, 0, 5);
        return TimeSpan.FromMilliseconds(Math.Min(30_000, baseMilliseconds * multiplier));
    }

    internal int FailureThreshold =>
        Math.Clamp(CircuitBreakerFailureThreshold, 1, 100);

    internal TimeSpan BreakDuration =>
        TimeSpan.FromSeconds(Math.Clamp(CircuitBreakerBreakSeconds, 1, 3600));
}

public sealed class LlmHttpTransport : ILlmTransport
{
    public const string HttpClientName = "FlowOS.LlmTransport";

    private static readonly string[] RequestIdHeaders =
        ["x-request-id", "request-id", "openai-request-id"];

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly LlmTransportOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly ConcurrentDictionary<string, CircuitState> _circuits =
        new(StringComparer.OrdinalIgnoreCase);

    public LlmHttpTransport(
        IHttpClientFactory httpClientFactory,
        LlmTransportOptions options,
        TimeProvider? timeProvider = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _delay = delay ?? ((duration, cancellationToken) =>
            Task.Delay(duration, _timeProvider, cancellationToken));
    }

    public async Task<LlmTransportResult> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        BufferedRequest buffered;
        try
        {
            buffered = await BufferedRequest.CreateAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Failure(AgentFailureCodes.ProviderUnavailable);
        }

        var circuit = _circuits.GetOrAdd(buffered.CircuitKey, _ => new CircuitState());
        if (circuit.IsOpen(_timeProvider.GetUtcNow()))
            return Failure(AgentFailureCodes.ProviderUnavailable, attemptCount: 0);

        var client = _httpClientFactory.CreateClient(HttpClientName);
        var maximumAttempts = _options.RetryAttempts + 1;
        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var attemptTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptTimeout.CancelAfter(_options.Timeout);
            using var outbound = buffered.CreateMessage();

            try
            {
                using var response = await client.SendAsync(
                    outbound,
                    HttpCompletionOption.ResponseHeadersRead,
                    attemptTimeout.Token);
                var statusCode = (int)response.StatusCode;
                var requestId = ReadRequestId(response);
                var transient = IsTransientStatus(statusCode);

                if (transient && attempt < maximumAttempts)
                {
                    await DelayBeforeRetryAsync(attempt, cancellationToken);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    if (transient)
                        circuit.RecordFailure(
                            _timeProvider.GetUtcNow(),
                            _options.FailureThreshold,
                            _options.BreakDuration);
                    else
                        circuit.RecordSuccess();

                    return new LlmTransportResult(
                        false,
                        null,
                        statusCode,
                        requestId,
                        FailureCode(statusCode),
                        attempt);
                }

                var content = await response.Content.ReadAsStringAsync(attemptTimeout.Token);
                circuit.RecordSuccess();
                return new LlmTransportResult(
                    true,
                    content,
                    statusCode,
                    requestId,
                    null,
                    attempt);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                if (attempt < maximumAttempts)
                {
                    await DelayBeforeRetryAsync(attempt, cancellationToken);
                    continue;
                }

                circuit.RecordFailure(
                    _timeProvider.GetUtcNow(),
                    _options.FailureThreshold,
                    _options.BreakDuration);
                return Failure(AgentFailureCodes.ProviderTimeout, attemptCount: attempt);
            }
            catch (HttpRequestException)
            {
                if (attempt < maximumAttempts)
                {
                    await DelayBeforeRetryAsync(attempt, cancellationToken);
                    continue;
                }

                circuit.RecordFailure(
                    _timeProvider.GetUtcNow(),
                    _options.FailureThreshold,
                    _options.BreakDuration);
                return Failure(AgentFailureCodes.ProviderUnavailable, attemptCount: attempt);
            }
            catch
            {
                circuit.RecordFailure(
                    _timeProvider.GetUtcNow(),
                    _options.FailureThreshold,
                    _options.BreakDuration);
                return Failure(AgentFailureCodes.ProviderUnavailable, attemptCount: attempt);
            }
        }

        return Failure(AgentFailureCodes.ProviderUnavailable);
    }

    private Task DelayBeforeRetryAsync(
        int failedAttempt,
        CancellationToken cancellationToken)
    {
        var delay = _options.RetryDelay(failedAttempt);
        return delay <= TimeSpan.Zero
            ? Task.CompletedTask
            : _delay(delay, cancellationToken);
    }

    private static bool IsTransientStatus(int statusCode) =>
        statusCode == 408 ||
        statusCode == 429 ||
        statusCode >= 500;

    private static string FailureCode(int statusCode) =>
        statusCode switch
        {
            401 or 403 => AgentFailureCodes.ProviderAuth,
            429 => AgentFailureCodes.ProviderRateLimit,
            408 => AgentFailureCodes.ProviderTimeout,
            _ => AgentFailureCodes.ProviderUnavailable
        };

    private static LlmTransportResult Failure(
        string failureCode,
        int attemptCount = 1) =>
        new(false, null, null, null, failureCode, attemptCount);

    private static string? ReadRequestId(HttpResponseMessage response)
    {
        foreach (var header in RequestIdHeaders)
        {
            if (!response.Headers.TryGetValues(header, out var values))
                continue;

            var sanitized = LlmTelemetrySanitizer.SanitizeRequestId(values.FirstOrDefault());
            if (sanitized != null)
                return sanitized;
        }

        return null;
    }

    private sealed class CircuitState
    {
        private readonly object _sync = new();
        private int _consecutiveFailures;
        private DateTimeOffset? _openUntil;

        public bool IsOpen(DateTimeOffset now)
        {
            lock (_sync)
            {
                if (!_openUntil.HasValue)
                    return false;
                if (_openUntil.Value > now)
                    return true;

                _openUntil = null;
                _consecutiveFailures = 0;
                return false;
            }
        }

        public void RecordSuccess()
        {
            lock (_sync)
            {
                _consecutiveFailures = 0;
                _openUntil = null;
            }
        }

        public void RecordFailure(
            DateTimeOffset now,
            int failureThreshold,
            TimeSpan breakDuration)
        {
            lock (_sync)
            {
                _consecutiveFailures++;
                if (_consecutiveFailures >= failureThreshold)
                    _openUntil = now.Add(breakDuration);
            }
        }
    }

    private sealed record BufferedRequest(
        HttpMethod Method,
        Uri RequestUri,
        Version Version,
        HttpVersionPolicy VersionPolicy,
        IReadOnlyList<KeyValuePair<string, IEnumerable<string>>> Headers,
        byte[]? Content,
        IReadOnlyList<KeyValuePair<string, IEnumerable<string>>> ContentHeaders)
    {
        public string CircuitKey => RequestUri.GetLeftPart(UriPartial.Authority);

        public static async Task<BufferedRequest> CreateAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri == null)
                throw new InvalidOperationException("LLM request URI is required.");

            var content = request.Content == null
                ? null
                : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            return new BufferedRequest(
                request.Method,
                request.RequestUri,
                request.Version,
                request.VersionPolicy,
                request.Headers
                    .Select(header => new KeyValuePair<string, IEnumerable<string>>(
                        header.Key,
                        header.Value.ToArray()))
                    .ToArray(),
                content,
                request.Content?.Headers
                    .Select(header => new KeyValuePair<string, IEnumerable<string>>(
                        header.Key,
                        header.Value.ToArray()))
                    .ToArray()
                    ?? Array.Empty<KeyValuePair<string, IEnumerable<string>>>());
        }

        public HttpRequestMessage CreateMessage()
        {
            var message = new HttpRequestMessage(Method, RequestUri)
            {
                Version = Version,
                VersionPolicy = VersionPolicy
            };
            foreach (var header in Headers)
                message.Headers.TryAddWithoutValidation(header.Key, header.Value);

            if (Content != null)
            {
                message.Content = new ByteArrayContent(Content);
                foreach (var header in ContentHeaders)
                    message.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return message;
        }
    }
}
