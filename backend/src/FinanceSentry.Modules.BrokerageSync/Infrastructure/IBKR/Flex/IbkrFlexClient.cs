using System.Net;
using System.Net.Http.Headers;
using System.Xml;
using System.Xml.Serialization;
using FinanceSentry.Modules.BrokerageSync.Domain.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinanceSentry.Modules.BrokerageSync.Infrastructure.IBKR.Flex;

public interface IIbkrFlexClient
{
    Task<FlexStatementXml> FetchStatementAsync(
        IbkrFlexCredentials credentials, FlexStatementWindow? window = null, CancellationToken ct = default);
}

/// <summary>
/// Talks to IBKR's Flex Web Service: <c>SendRequest</c> kicks off statement generation for a
/// saved Activity Flex Query, then <c>GetStatement</c> is polled with the returned reference
/// code until the statement is ready. Every call — the initial request and every poll — goes
/// through <see cref="IbkrFlexRateLimiter"/>, since the 1 req/s + 10 req/min limit is per token,
/// not per endpoint. <c>SendRequest</c> answered with "statement is not available" (1003) or "generation in
/// progress" (1019) is retried with a bounded backoff (<see cref="IbkrFlexOptions.SendRequestMaxAttempts"/>);
/// every refusal is logged with IBKR's error code.
/// </summary>
public sealed class IbkrFlexClient(
    HttpClient http,
    IOptions<IbkrFlexOptions> options,
    IbkrFlexRateLimiter rateLimiter,
    ILogger<IbkrFlexClient> logger) : IIbkrFlexClient
{
    private const string FlexNotYetGeneratedErrorCode = "1019";

    private static readonly HashSet<string> RetryableSendRequestCodes =
        [IbkrFlexException.StatementNotAvailableCode, FlexNotYetGeneratedErrorCode];

    // Same edge quirk as the OAuth client: no User-Agent or HTTP/2 gets a silent 403.
    private static readonly ProductInfoHeaderValue UserAgent = new("finance-sentry", "1.0");

    private static readonly XmlSerializer ErrorSerializer = new(typeof(FlexStatementResponseXml));
    private static readonly XmlSerializer StatementSerializer = new(typeof(FlexQueryResponseXml));

    public async Task<FlexStatementXml> FetchStatementAsync(
        IbkrFlexCredentials credentials, FlexStatementWindow? window = null, CancellationToken ct = default)
    {
        var referenceCode = await SendRequestAsync(credentials, window, ct);

        for (var attempt = 1; attempt <= options.Value.MaxPollAttempts; attempt++)
        {
            var statement = await TryGetStatementAsync(credentials, referenceCode, ct);
            if (statement is not null)
                return statement.FlexStatements.Items.SingleOrDefault()
                    ?? throw new IbkrFlexException(null, "IBKR Flex statement contained no FlexStatement entries.");

            logger.LogInformation(
                "IBKR Flex statement for reference {ReferenceCode} not yet generated (attempt {Attempt}/{MaxAttempts})",
                referenceCode, attempt, options.Value.MaxPollAttempts);

            if (attempt < options.Value.MaxPollAttempts)
                await Task.Delay(options.Value.PollInterval, ct);
        }

        throw new IbkrFlexException(
            FlexNotYetGeneratedErrorCode,
            $"IBKR Flex statement for reference {referenceCode} was not ready after {options.Value.MaxPollAttempts} polls.");
    }

    private async Task<string> SendRequestAsync(IbkrFlexCredentials credentials, FlexStatementWindow? window, CancellationToken ct)
    {
        var url = $"{options.Value.BaseUrl.TrimEnd('/')}/SendRequest?t={Uri.EscapeDataString(credentials.Token)}" +
                  $"&q={Uri.EscapeDataString(credentials.QueryId)}&v={options.Value.Version}";

        if (window is not null)
            url += $"&fd={window.FromDateWire}&td={window.ToDateWire}";

        var maxAttempts = Math.Max(1, options.Value.SendRequestMaxAttempts);
        var delay = options.Value.SendRequestRetryDelay;
        for (var attempt = 1; ; attempt++)
        {
            var body = await SendAsync(url, ct);
            var ack = DeserializeError(body);

            if (string.Equals(ack.Status, "Success", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(ack.ReferenceCode))
                return ack.ReferenceCode;

            var retryable = ack.ErrorCode is not null && RetryableSendRequestCodes.Contains(ack.ErrorCode);
            if (!retryable || attempt >= maxAttempts)
            {
                logger.LogWarning(
                    "IBKR Flex SendRequest refused (code {FlexErrorCode}: {FlexMessage}) for window {FromDate}-{ToDate} after {Attempt} attempt(s)",
                    ack.ErrorCode, ack.ErrorMessage, window?.FromDateWire, window?.ToDateWire, attempt);
                throw new IbkrFlexException(ack.ErrorCode, ack.ErrorMessage ?? "IBKR Flex SendRequest failed.");
            }

            logger.LogInformation(
                "IBKR Flex SendRequest answered code {FlexErrorCode} ({FlexMessage}) for window {FromDate}-{ToDate}; retrying in {Delay} (attempt {Attempt}/{MaxAttempts})",
                ack.ErrorCode, ack.ErrorMessage, window?.FromDateWire, window?.ToDateWire, delay, attempt, maxAttempts);
            await Task.Delay(delay, ct);
            delay *= 2;
        }
    }

    private async Task<FlexQueryResponseXml?> TryGetStatementAsync(
        IbkrFlexCredentials credentials, string referenceCode, CancellationToken ct)
    {
        var url = $"{options.Value.BaseUrl.TrimEnd('/')}/GetStatement?t={Uri.EscapeDataString(credentials.Token)}" +
                  $"&q={Uri.EscapeDataString(referenceCode)}&v={options.Value.Version}";

        var body = await SendAsync(url, ct);
        var rootName = PeekRootElementName(body);

        if (rootName == "FlexQueryResponse")
            return DeserializeStatement(body);

        var error = DeserializeError(body);
        if (error.ErrorCode == FlexNotYetGeneratedErrorCode)
            return null;

        throw new IbkrFlexException(error.ErrorCode, error.ErrorMessage ?? "IBKR Flex GetStatement failed.");
    }

    private async Task<string> SendAsync(string url, CancellationToken ct)
    {
        await rateLimiter.WaitAsync(ct);

        using var request = new HttpRequestMessage(HttpMethod.Get, url)
        {
            Version = HttpVersion.Version11,
        };
        request.Headers.UserAgent.Add(UserAgent);

        using var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new IbkrFlexException(null, $"IBKR Flex rejected the request ({(int)response.StatusCode}).", response.StatusCode);

        response.EnsureSuccessStatusCode();
        return body;
    }

    private static string PeekRootElementName(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml));
        reader.MoveToContent();
        return reader.Name;
    }

    private static FlexStatementResponseXml DeserializeError(string xml)
    {
        using var reader = new StringReader(xml);
        return (FlexStatementResponseXml?)ErrorSerializer.Deserialize(reader)
            ?? throw new IbkrFlexException(null, "IBKR Flex returned an empty response.");
    }

    public static FlexQueryResponseXml DeserializeStatement(string xml)
    {
        using var reader = new StringReader(xml);
        var response = (FlexQueryResponseXml?)StatementSerializer.Deserialize(reader)
            ?? throw new IbkrFlexException(null, "IBKR Flex returned an empty statement.");
        foreach (var statement in response.FlexStatements.Items)
            statement.NormalizeAbsentSections();
        return response;
    }
}
