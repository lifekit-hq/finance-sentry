namespace FinanceSentry.Modules.Companion.Application.Services;

using FinanceSentry.Modules.Companion.Domain;

/// <summary>Derives a short device label ("Chrome on Android") from a User-Agent for the device list.</summary>
public static class PushDeviceLabel
{
    private static readonly (string Token, string Name)[] Browsers =
    [
        ("Edg/", "Edge"), ("Firefox/", "Firefox"), ("CriOS/", "Chrome"), ("FxiOS/", "Firefox"),
        ("Chrome/", "Chrome"), ("Safari/", "Safari"),
    ];

    private static readonly (string Token, string Name)[] Platforms =
    [
        ("iPhone", "iPhone"), ("iPad", "iPad"), ("Android", "Android"), ("Windows", "Windows"),
        ("Macintosh", "Mac"), ("Linux", "Linux"),
    ];

    public static string? FromUserAgent(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return null;

        var browser = Browsers.FirstOrDefault(b => userAgent.Contains(b.Token, StringComparison.Ordinal)).Name;
        var platform = Platforms.FirstOrDefault(p => userAgent.Contains(p.Token, StringComparison.Ordinal)).Name;

        var label = (browser, platform) switch
        {
            (not null, not null) => $"{browser} on {platform}",
            (not null, null) => browser,
            (null, not null) => platform,
            _ => null,
        };
        return label is { Length: > PushSubscriptionLimits.DeviceLabelMaxLength }
            ? label[..PushSubscriptionLimits.DeviceLabelMaxLength]
            : label;
    }
}
