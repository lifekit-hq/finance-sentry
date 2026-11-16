namespace FinanceSentry.Modules.Research.Application.Services;

using Microsoft.Extensions.Options;

/// <summary>
/// Refuses to send any request on the <c>sec-edgar</c> client unless <c>SecEdgar:Contact</c> is an email
/// address: the SEC edge answers HTTP 403 to every other User-Agent (#1443), so sending would only
/// get the host blocked. The failure surfaces through <see cref="EdgarTickerMap"/>'s health report.
/// </summary>
public sealed class SecContactGuardHandler(IOptions<SecEdgarOptions> options) : DelegatingHandler
{
    internal const string ContactNotConfigured =
        "SecEdgar:Contact is missing or is not an email address. The SEC refuses (HTTP 403) every request "
        + "whose User-Agent does not carry one, so no ticker->CIK map, filing or fundamentals can load. "
        + "Set SEC_EDGAR_CONTACT (SecEdgar__Contact) to an email address the SEC may reach the operator on.";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => SecEdgarOptions.IsEmailContact(options.Value.Contact)
            ? base.SendAsync(request, cancellationToken)
            : throw new InvalidOperationException(ContactNotConfigured);
}
