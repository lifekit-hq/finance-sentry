namespace FinanceSentry.Modules.Alerts.Domain;

using FinanceSentry.Core.Utils;

public static class AlertAppPaths
{
    /// <summary>The alert's stored path, or the one resolved from what it carries for alerts stored before the path existed.</summary>
    public static string? For(Alert alert)
        => alert.AppPath ?? AlertAppPath.Resolve(alert.Type, alert.ReferenceId, alert.ReferenceLabel, alert.CreatedAt);
}
