namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Modules.Research.Application.Services.Catalyst;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Spaces every request on the <c>sec-edgar</c> client under the SEC fair-access limit (10 requests a second
/// per address, <c>SecEdgar:RequestsPerSecond</c>). Fundamentals fetch each concept's tags four at a time and
/// answer in ~10 ms, so an unpaced thesis-monitor run bursts past the limit: the SEC answers 429, the client's
/// circuit breaker opens, and every remaining fetch fails as "EDGAR concept fetches failed", which the monitor
/// reads as INFRA_BLIND and alerts as coverage degraded. It sits innermost (after the resilience handler), so
/// a retried attempt is paced too, and it bounds the whole client whatever else paces its own calls.
/// </summary>
public sealed class SecRequestPacingHandler([FromKeyedServices(SecRequestPacingHandler.PacerKey)] RequestPacer pacer)
    : DelegatingHandler
{
    public const string PacerKey = "sec-edgar-client";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await pacer.WaitAsync(cancellationToken);
        return await base.SendAsync(request, cancellationToken);
    }
}
