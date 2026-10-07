namespace FinanceSentry.Modules.Companion.Application.Services;

using System.Text.Json;
using System.Text.RegularExpressions;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Companion.Domain;

/// <summary>
/// The lock-screen notification (spec 859 FR-003): the kind, the subject and a deep link, nothing else. Never the
/// event summary, severity, amounts or merchants; detail appears after the tap, inside the authenticated app. Shaped for
/// the Angular service worker (<c>{"notification":{…}}</c>), which shows it and opens the link on click. The link is the
/// event's own <see cref="CompanionEvent.AppPath"/> (the thing it is about), or the alerts page when it has none.
/// </summary>
public static partial class PushPayload
{
    /// <summary>Where a notification opens when its event has no target of its own.</summary>
    public const string DeepLink = "/alerts";

    public static string Build(CompanionEvent evt)
    {
        var body = string.IsNullOrWhiteSpace(evt.Subject) ? null : evt.Subject.Trim();
        var notification = new Dictionary<string, object>
        {
            ["title"] = KindLabel(evt.Kind),
            ["tag"] = evt.Id.ToString(),
            ["data"] = new
            {
                onActionClick = new
                {
                    @default = new { operation = "navigateLastFocusedOrOpen", url = LinkFor(evt) },
                },
            },
        };
        if (body is not null)
            notification["body"] = body;

        return JsonSerializer.Serialize(new { notification }, JsonOptions);
    }

    /// <summary>The lock-screen notification for an alert pushed without a companion event: its headline ("Analyst upgrade")
    /// as the title, its subject (the ticker) as the body and its own app path as the link.</summary>
    public static string BuildAlert(MaterialAlertRecord alert)
    {
        var colon = alert.Title.IndexOf(':', StringComparison.Ordinal);
        var title = (colon > 0 ? alert.Title[..colon] : alert.Title).Trim();
        var notification = new Dictionary<string, object>
        {
            ["title"] = title,
            ["tag"] = alert.AlertId.ToString(),
            ["data"] = new
            {
                onActionClick = new
                {
                    @default = new { operation = "navigateLastFocusedOrOpen", url = LinkFor(alert.AppPath) },
                },
            },
        };
        if (!string.IsNullOrWhiteSpace(alert.ReferenceLabel))
            notification["body"] = alert.ReferenceLabel.Trim();

        return JsonSerializer.Serialize(new { notification }, JsonOptions);
    }

    /// <summary>The event's own path when it is a path inside this app, otherwise <see cref="DeepLink"/>.</summary>
    public static string LinkFor(CompanionEvent evt) => LinkFor(evt.AppPath);

    private static string LinkFor(string? appPath)
        => appPath is { } path && path.StartsWith('/') && !path.StartsWith("//", StringComparison.Ordinal)
            ? path
            : DeepLink;

    /// <summary>"LowBalance" becomes "Low balance".</summary>
    public static string KindLabel(CompanionEventKind kind)
    {
        var words = WordBoundary().Replace(kind.ToString(), " ");
        return words.Length == 0 ? words : string.Concat(char.ToUpperInvariant(words[0]), words[1..].ToLowerInvariant());
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [GeneratedRegex("(?<=[a-z])(?=[A-Z])")]
    private static partial Regex WordBoundary();
}
