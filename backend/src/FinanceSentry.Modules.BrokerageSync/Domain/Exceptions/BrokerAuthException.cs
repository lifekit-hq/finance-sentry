using System.Net;
using FinanceSentry.Core.Exceptions;

namespace FinanceSentry.Modules.BrokerageSync.Domain.Exceptions;

public sealed class BrokerAuthException : ApiException
{
    public string? BrokerName { get; }

    /// <summary>
    /// The upstream broker's HTTP status code, when the failure came from a broker HTTP
    /// response (as opposed to a local validation failure). Lets callers tell a transient
    /// upstream blip (5xx) apart from a genuine credential/consent problem (401/403),
    /// even though both surface here as the same 422 INVALID_CREDENTIALS API error.
    /// </summary>
    public HttpStatusCode? UpstreamStatusCode { get; }

    public BrokerAuthException(string message, string? brokerName = null, HttpStatusCode? upstreamStatusCode = null)
        : base(422, "INVALID_CREDENTIALS", message)
    {
        BrokerName = brokerName;
        UpstreamStatusCode = upstreamStatusCode;
    }

    public BrokerAuthException(string message, string? brokerName, Exception inner)
        : base(422, "INVALID_CREDENTIALS", message, inner)
    {
        BrokerName = brokerName;
    }
}
