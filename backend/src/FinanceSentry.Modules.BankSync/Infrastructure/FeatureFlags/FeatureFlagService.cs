namespace FinanceSentry.Modules.BankSync.Infrastructure.FeatureFlags;

using Microsoft.Extensions.Options;

/// <summary>
/// Simple feature flag service backed by IConfiguration (appsettings / env vars).
/// Keys are read from "FeatureFlags:{KEY}" — e.g. "FeatureFlags:BANK_SYNC_ENABLED=true".
/// Supports gradual rollout by user ID hash when a percentage value (0-100) is configured.
/// </summary>
public interface IFeatureFlagService
{
    /// <summary>Returns true if the flag is enabled globally.</summary>
    bool IsEnabled(string flagKey);

    /// <summary>
    /// Returns true if the flag is enabled for a specific user.
    /// Supports percentage-based rollout: "FeatureFlags:BANK_SYNC_ENABLED=50" → enabled for 50% of users.
    /// </summary>
    bool IsEnabledForUser(string flagKey, Guid userId);
}

/// <summary>
/// The <c>FeatureFlags</c> configuration section: flag key → raw value ("true"/"false" or a 0-100 percentage).
/// Keys are case-insensitive, as configuration keys are. Optional — an unset flag is off.
/// </summary>
public sealed class FeatureFlagOptions : Dictionary<string, string?>
{
    public const string SectionName = "FeatureFlags";

    public FeatureFlagOptions()
        : base(StringComparer.OrdinalIgnoreCase)
    {
    }
}

public class FeatureFlagService(IOptionsMonitor<FeatureFlagOptions> flags) : IFeatureFlagService
{
    private string? Lookup(string flagKey) => flags.CurrentValue.GetValueOrDefault(flagKey);

    public bool IsEnabled(string flagKey)
    {
        var value = Lookup(flagKey);
        if (value is null) return false;

        // Boolean flag: "true" / "false"
        if (bool.TryParse(value, out var boolVal))
            return boolVal;

        // Percentage flag: "100" means fully enabled globally
        if (int.TryParse(value, out var pct))
            return pct >= 100;

        return false;
    }

    public bool IsEnabledForUser(string flagKey, Guid userId)
    {
        var value = Lookup(flagKey);
        if (value is null) return false;

        if (bool.TryParse(value, out var boolVal))
            return boolVal;

        // Percentage-based rollout: hash userId to 0-99, compare to threshold
        if (int.TryParse(value, out var pct))
        {
            var userBucket = Math.Abs(userId.GetHashCode() % 100);
            return userBucket < pct;
        }

        return false;
    }
}
