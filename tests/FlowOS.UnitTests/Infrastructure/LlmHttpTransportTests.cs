using System.Net;
using FlowOS.Agents.Abstractions;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Infrastructure;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;

namespace FlowOS.UnitTests.Infrastructure;

public sealed class LlmHttpTransportTests
{
    [Fact]
    public void Registration_UsesNamedClientAndConfiguredTimeout()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FlowOS:Agents:LlmTransport:TimeoutSeconds"] = "17",
                ["FlowOS:Agents:LlmTransport:MaxRetryAttempts"] = "4"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(x => x.EnvironmentName).Returns(Environments.Development);
        services.AddSingleton(environment.Object);
        services.AddDbContext<FlowOSDbContext>(options =>
            options.UseInMemoryDatabase($"llm-transport-di-{Guid.NewGuid():N}"));
        services.AddFlowOSPersistence();
        using var provider = services.BuildServiceProvider();

        var client = provider
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(LlmHttpTransport.HttpClientName);
        using var scope = provider.CreateScope();

        Assert.Equal(TimeSpan.FromSeconds(17), client.Timeout);
        Assert.Equal(
            4,
            provider.GetRequiredService<LlmTransportOptions>().MaxRetryAttempts);
        Assert.IsType<LlmHttpTransport>(
            provider.GetRequiredService<ILlmTransport>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWorkflowAgentFactory>());
    }

    [Fact]
    public async Task Transport_RetriesOnlyBoundedTransientResponses()
    {
        var handler = new SequenceHandler(
            _ => Response(HttpStatusCode.ServiceUnavailable),
            _ => Response(HttpStatusCode.TooManyRequests),
            _ => Response(HttpStatusCode.OK, """{"ok":true}"""));
        var transport = CreateTransport(
            handler,
            new LlmTransportOptions
            {
                MaxRetryAttempts = 2,
                BaseRetryDelayMilliseconds = 0,
                CircuitBreakerFailureThreshold = 5
            });
        using var request = Request();

        var result = await transport.SendAsync(request);

        Assert.True(result.Success);
        Assert.Equal(3, result.AttemptCount);
        Assert.Equal(3, handler.CallCount);
        Assert.Equal("""{"ok":true}""", result.Content);
    }

    [Fact]
    public async Task Transport_OpensCircuitAfterConfiguredFailures()
    {
        var handler = new SequenceHandler(_ => Response(HttpStatusCode.ServiceUnavailable));
        var transport = CreateTransport(
            handler,
            new LlmTransportOptions
            {
                MaxRetryAttempts = 0,
                BaseRetryDelayMilliseconds = 0,
                CircuitBreakerFailureThreshold = 2,
                CircuitBreakerBreakSeconds = 60
            });

        using var firstRequest = Request();
        using var secondRequest = Request();
        using var blockedRequest = Request();
        Assert.False((await transport.SendAsync(firstRequest)).Success);
        Assert.False((await transport.SendAsync(secondRequest)).Success);
        var blocked = await transport.SendAsync(blockedRequest);

        Assert.False(blocked.Success);
        Assert.Equal(AgentFailureCodes.ProviderUnavailable, blocked.FailureCode);
        Assert.Equal(0, blocked.AttemptCount);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Transport_ClassifiesTimeoutWithoutLeakingExceptionDetails()
    {
        var handler = new SequenceHandler(
            _ => throw new TaskCanceledException("https://example.test?key=must-not-leak"));
        var transport = CreateTransport(
            handler,
            new LlmTransportOptions
            {
                MaxRetryAttempts = 1,
                BaseRetryDelayMilliseconds = 0
            });
        using var request = Request();

        var result = await transport.SendAsync(request);

        Assert.False(result.Success);
        Assert.Equal(AgentFailureCodes.ProviderTimeout, result.FailureCode);
        Assert.Equal(2, result.AttemptCount);
        Assert.Null(result.Content);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Transport_DropsUnsafeProviderRequestIdentifiers()
    {
        var handler = new SequenceHandler(_ =>
        {
            var response = Response(HttpStatusCode.OK);
            response.Headers.TryAddWithoutValidation(
                "x-request-id",
                "secret=value&must-not-surface");
            return response;
        });
        var transport = CreateTransport(handler, new LlmTransportOptions());
        using var request = Request();

        var result = await transport.SendAsync(request);

        Assert.True(result.Success);
        Assert.Null(result.ProviderRequestId);
    }

    private static LlmHttpTransport CreateTransport(
        HttpMessageHandler handler,
        LlmTransportOptions options)
    {
        var client = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        return new LlmHttpTransport(new StubHttpClientFactory(client), options);
    }

    private static HttpRequestMessage Request()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://provider.example/v1/messages")
        {
            Content = new StringContent("""{"prompt":"safe"}""")
        };
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "secret");
        return request;
    }

    private static HttpResponseMessage Response(
        HttpStatusCode statusCode,
        string body = "{}") =>
        new(statusCode)
        {
            Content = new StringContent(body)
        };

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public StubHttpClientFactory(HttpClient client) => _client = client;

        public HttpClient CreateClient(string name)
        {
            Assert.Equal(LlmHttpTransport.HttpClientName, name);
            return _client;
        }
    }

    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses;
        private readonly Func<HttpRequestMessage, HttpResponseMessage>? _fallback;

        public SequenceHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
        {
            _responses = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>(responses);
            if (responses.Length == 1)
                _fallback = responses[0];
        }

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            var response = _responses.Count > 0
                ? _responses.Dequeue()
                : _fallback ?? throw new InvalidOperationException("No response configured.");
            return Task.FromResult(response(request));
        }
    }
}
