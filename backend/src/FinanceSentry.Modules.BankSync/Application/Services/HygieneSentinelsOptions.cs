namespace FinanceSentry.Modules.BankSync.Application.Services;

/// <summary>
/// Tuning for the four 044 hygiene sentinels. Bound from the <c>HygieneSentinels</c> config section.
/// The defaults here are the contract: an absent section leaves every sentinel on its documented
/// setting, and a mistyped key now fails to bind loudly instead of silently reverting one job to a
/// literal nobody can grep for.
/// </summary>
public sealed class HygieneSentinelsOptions
{
    public const string SectionName = "HygieneSentinels";

    /// <summary>US1 — a subscription fires when it bills more than this fraction above its hike baseline.</summary>
    public decimal PriceHikeThreshold { get; set; } = 0.15m;

    /// <summary>US2 — how far back a repeat of the same merchant/amount charge still reads as a duplicate.</summary>
    public int DuplicateWindowDays { get; set; } = 5;

    /// <summary>US3 — month-to-date spend fires when it exceeds the category's baseline by this factor.</summary>
    public decimal CategorySpikeMultiplier { get; set; } = 2.0m;

    /// <summary>US4 — how far back conversions are re-examined on each tick.</summary>
    public int FxSpreadLookbackDays { get; set; } = 3;

    /// <summary>US4 — a conversion fires when it loses more than this fraction against the market rate.</summary>
    public decimal FxSpreadThreshold { get; set; } = 0.03m;

    /// <summary>
    /// US4 — how stale the FX rate table may be before the sentinel stands down rather than measuring
    /// a spread against the offline seed. The refresh runs daily, so 48h tolerates one missed run.
    /// Non-positive is never fresh, which is also the sentinel's off switch.
    /// </summary>
    public int FxSpreadMaxRateAgeHours { get; set; } = 48;
}
