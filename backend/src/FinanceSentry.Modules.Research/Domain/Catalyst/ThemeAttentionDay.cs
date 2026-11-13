namespace FinanceSentry.Modules.Research.Domain.Catalyst;

/// <summary>
/// One day of news-article volume for one phrase of one reference-universe theme, as GDELT counted it: the stored attention
/// measure behind the "attention is a penalty or staging input, never the reason to add" rule. Shared market data, not
/// per-user: no <c>UserId</c>, so the owner query filter does not apply. A refresh replaces the rows it re-reads (the
/// newest day is partial until the day ends), keyed by theme, phrase and day.
/// </summary>
public sealed class ThemeAttentionDay
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The reference list key of the theme (<c>theme:memory</c>).</summary>
    public string Theme { get; set; } = string.Empty;

    /// <summary>The phrase counted (<c>HBM memory</c>).</summary>
    public string Phrase { get; set; } = string.Empty;

    /// <summary>The UTC day.</summary>
    public DateOnly Day { get; set; }

    /// <summary>Articles matching the phrase that day.</summary>
    public long Articles { get; set; }

    /// <summary>All articles GDELT monitored that day (the normaliser); null when the response carried none.</summary>
    public long? TotalArticles { get; set; }

    public string Source { get; set; } = string.Empty;

    public DateTimeOffset MeasuredAt { get; set; } = DateTimeOffset.UtcNow;
}
