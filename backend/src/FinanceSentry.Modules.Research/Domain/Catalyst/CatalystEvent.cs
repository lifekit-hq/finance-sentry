namespace FinanceSentry.Modules.Research.Domain.Catalyst;

using FinanceSentry.Core.Auth;

/// <summary>What the catalyst detection did about a filing it found on a reference-universe name.</summary>
public enum CatalystDisposition
{
    /// <summary>The name was outside coverage and was nominated into the candidate path, tagged as a catalyst nomination.</summary>
    Nominated,

    /// <summary>The name is already covered: the filing was handed to the coverage event review (stage 4) instead of a nomination.</summary>
    Covered,

    /// <summary>The name left coverage after this filing was dated, and the filing is no newer than the removal, so it is no reason to bring the name back.</summary>
    SkippedRemoved,
}

/// <summary>
/// One dated 8-K catalyst (a filing carrying an item the catalyst scan watches) on a reference-universe name and what the
/// pipeline did about it: the discovery-funnel "catalyst detection beyond the ticker feed" stage. The row is the once-only
/// identity of the filing for the user (ticker and accession number, which EDGAR never reuses), so a daily re-read of the same
/// filing is a no-op and never nominates twice. The filing facts are public; the disposition is the user's pipeline's. No model
/// is consulted anywhere on this path.
/// </summary>
public class CatalystEvent : IUserOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    /// <summary>Upper-case Yahoo-form ticker of the name the filing belongs to.</summary>
    public string Ticker { get; set; } = string.Empty;

    /// <summary>EDGAR accession number (<c>0001045450-26-000123</c>).</summary>
    public string Accession { get; set; } = string.Empty;

    /// <summary>The form (<c>8-K</c>).</summary>
    public string Form { get; set; } = string.Empty;

    /// <summary>The watched items the filing carries, comma separated in the order the scan ranks them (<c>2.02,1.01</c>).</summary>
    public string Items { get; set; } = string.Empty;

    /// <summary>The date EDGAR dates the filing.</summary>
    public DateOnly FilingDate { get; set; }

    /// <summary>The catalyst in words ("8-K item 2.02 filed 2026-11-12").</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>The filing's document on EDGAR; empty when the source gave none.</summary>
    public string DocumentUrl { get; set; } = string.Empty;

    /// <summary>Which feed found it: <c>fulltext</c> (EDGAR full-text search) or <c>submissions</c> (the per-ticker fallback).</summary>
    public string Source { get; set; } = string.Empty;

    public DateTimeOffset DetectedAt { get; set; } = DateTimeOffset.UtcNow;

    public CatalystDisposition Disposition { get; set; }

    /// <summary>The nomination tag written on the candidate (<see cref="CatalystRules.Tag"/>); null unless the filing was nominated.</summary>
    public string? Tag { get; set; }

    /// <summary>The decision in words: why a filing was covered-only or skipped; the nomination's tag otherwise.</summary>
    public string Detail { get; set; } = string.Empty;
}
