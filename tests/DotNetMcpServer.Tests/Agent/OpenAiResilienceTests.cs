using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using DotNetMcpServer.Agent.Config;
using DotNetMcpServer.Agent.Llm;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace DotNetMcpServer.Tests.Agent;

/// <summary>
/// A rate-limited OpenAI response must not reach the user. These tests resolve the real typed
/// client from the real registration and drive it through the real resilience pipeline —
/// only the transport at the bottom is replaced, so what is proven here is the shipped wiring.
/// </summary>
public sealed class OpenAiResilienceTests
{
    /// <summary>
    /// Stands in for the OpenAI endpoint. Responses are returned in order and the last one
    /// repeats, so "always rate limited" needs no attempt count up front.
    /// </summary>
    private sealed class StubTransport : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage>[] _responses;
        private int _attempts;

        public StubTransport(params Func<HttpResponseMessage>[] responses)
        {
            _responses = responses;
        }

        public int Attempts => Volatile.Read(ref _attempts);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var index = Interlocked.Increment(ref _attempts) - 1;
            return Task.FromResult(_responses[Math.Min(index, _responses.Length - 1)]());
        }
    }

    /// <remarks>
    /// A <c>Retry-After</c> of zero keeps the test fast, and is also the point: without the
    /// header driving the delay these tests would sit through the pipeline's backoff schedule.
    /// </remarks>
    private static HttpResponseMessage RateLimited() => new(HttpStatusCode.TooManyRequests)
    {
        Headers = { RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero) },
        Content = new StringContent("""{"error":{"message":"rate limit reached"}}""", Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage BadRequest() => new(HttpStatusCode.BadRequest)
    {
        Content = new StringContent("""{"error":{"message":"unknown model"}}""", Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage Completion(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            $$$"""{"choices":[{"message":{"role":"assistant","content":"{{{content}}}"}}]}""",
            Encoding.UTF8,
            "application/json")
    };

    private static ServiceProvider BuildProvider(HttpMessageHandler transport)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IOptions<OpenAiSettings>>(Options.Create(new OpenAiSettings
        {
            ApiKey = "sk-test-key",
            BaseUrl = "https://openai.invalid/v1"
        }));

        services.AddOpenAiChatClient().ConfigurePrimaryHttpMessageHandler(() => transport);

        return services.BuildServiceProvider();
    }

    private static JsonObject UserMessage(string content) => new()
    {
        ["role"] = "user",
        ["content"] = content
    };

    [Fact]
    public async Task A_rate_limited_response_is_retried_and_the_call_succeeds()
    {
        using var transport = new StubTransport(RateLimited, () => Completion("Retried transparently."));
        using var provider = BuildProvider(transport);

        var client = provider.GetRequiredService<OpenAiChatClient>();
        var turn = await client.CompleteAsync([UserMessage("hello")], [], CancellationToken.None);

        Assert.Equal("Retried transparently.", turn.Content);
        Assert.Equal(2, transport.Attempts);
    }

    [Fact]
    public async Task Retrying_stops_at_the_pipelines_attempt_limit_and_the_failure_surfaces()
    {
        using var transport = new StubTransport(RateLimited);
        using var provider = BuildProvider(transport);

        var client = provider.GetRequiredService<OpenAiChatClient>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.CompleteAsync([UserMessage("hello")], [], CancellationToken.None));

        Assert.Contains("429", exception.Message, StringComparison.Ordinal);

        // The first attempt plus the standard pipeline's three retries.
        Assert.Equal(4, transport.Attempts);
    }

    /// <summary>
    /// The retry predicate has to discriminate: a malformed request will fail identically on
    /// every attempt, so retrying it only multiplies the latency.
    /// </summary>
    [Fact]
    public async Task A_bad_request_is_not_retried()
    {
        using var transport = new StubTransport(BadRequest);
        using var provider = BuildProvider(transport);

        var client = provider.GetRequiredService<OpenAiChatClient>();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.CompleteAsync([UserMessage("hello")], [], CancellationToken.None));

        Assert.Equal(1, transport.Attempts);
    }

    /// <summary>
    /// Pins the deliberate departures from the standard defaults. Reverting any of them would
    /// cut a normal chat completion short at ten seconds.
    /// </summary>
    [Fact]
    public void The_pipeline_is_tuned_for_completion_latency_rather_than_for_a_fast_dependency()
    {
        var options = new HttpStandardResilienceOptions();

        OpenAiClientRegistration.ConfigureResilience(options);

        Assert.Equal(TimeSpan.FromSeconds(100), options.AttemptTimeout.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(300), options.TotalRequestTimeout.Timeout);

        // Enforced by the library's own validator at handler construction; asserted here so the
        // failure lands on a named test rather than on the agent's first request.
        Assert.True(options.CircuitBreaker.SamplingDuration >= options.AttemptTimeout.Timeout * 2);

        // Relied upon rather than set: this is what turns a 429's Retry-After into the delay.
        Assert.True(options.Retry.ShouldRetryAfterHeader);
    }
}
