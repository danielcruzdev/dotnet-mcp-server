using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace DotNetMcpServer.Agent.Llm;

public static class OpenAiClientRegistration
{
    /// <summary>
    /// Registers the typed OpenAI client behind <c>IHttpClientFactory</c>, wrapped in the
    /// standard resilience pipeline: total timeout, retry with jittered exponential backoff,
    /// circuit breaker, and a per-attempt timeout.
    /// </summary>
    public static IHttpClientBuilder AddOpenAiChatClient(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The pipeline owns every timeout. HttpClient.Timeout sits above the handler chain and
        // would cap the whole send — retries included — at its 100-second default, silently
        // truncating the policy configured below.
        var builder = services.AddHttpClient<OpenAiChatClient>(
            client => client.Timeout = Timeout.InfiniteTimeSpan);

        builder.AddStandardResilienceHandler(ConfigureResilience);

        return builder;
    }

    /// <summary>
    /// Retry keeps its defaults — three attempts, exponential backoff with jitter, HTTP 429 and
    /// 5xx treated as transient, and the <c>Retry-After</c> header honoured when the server
    /// sends one. Only the values whose defaults are wrong for this caller are changed.
    /// </summary>
    public static void ConfigureResilience(HttpStandardResilienceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // The defaults — 10 s per attempt, 30 s overall — assume a fast internal dependency.
        // A chat completion carrying tool definitions routinely exceeds both.
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(100);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(300);

        // The breaker's default threshold is 100 requests per sampling window, which a
        // single-user console agent never reaches — leaving it decorative. The sampling
        // duration must stay at or above twice the attempt timeout, or the options fail
        // validation at handler construction.
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(240);
        options.CircuitBreaker.MinimumThroughput = 4;
        options.CircuitBreaker.FailureRatio = 0.5;
    }
}
