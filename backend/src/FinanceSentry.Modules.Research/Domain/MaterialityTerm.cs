namespace FinanceSentry.Modules.Research.Domain;

/// <summary>
/// A materiality keyword for <see cref="Infrastructure.Jobs.NewsMaterialityJob"/> (N1, #693): a hit on
/// <see cref="Term"/> fires the news-cluster detector regardless of source count. Persisted so an
/// operator can retune the list without a deploy — seeded once from the design report's original
/// keyword array (guidance, downgrade, investigation, M&amp;A, halted, recall, acquisition).
/// </summary>
public sealed class MaterialityTerm
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Term { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
