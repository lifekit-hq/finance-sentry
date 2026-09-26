namespace FinanceSentry.Modules.BrokerageSync.Infrastructure.IBKR.Flex;

/// <summary>
/// Configuration for the IBKR Flex Web Service — a different host and product from the
/// OAuth Web API (<see cref="OAuth.IbkrOAuthOptions"/>). Serves the four previous calendar
/// years plus year-to-date for historical Activity Flex Query pulls.
/// </summary>
public sealed class IbkrFlexOptions
{
    public const string SectionName = "IbkrFlex";

    public string BaseUrl { get; set; } = "https://ndcdyn.interactivebrokers.com/AccountManagement/FlexWebService";

    /// <summary>Flex Web Service API version, sent as the <c>v</c> query parameter on every call.</summary>
    public int Version { get; set; } = 3;

    /// <summary>
    /// How many times to poll <c>GetStatement</c> after a "statement not yet generated" response
    /// before giving up, and how long to wait between polls.
    /// </summary>
    public int MaxPollAttempts { get; set; } = 10;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);
}
