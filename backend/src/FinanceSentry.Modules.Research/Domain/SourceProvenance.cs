namespace FinanceSentry.Modules.Research.Domain;

/// <summary>
/// Where a fundamental came from (#837): the provider that supplied it, the document it was read
/// from (an EDGAR filing index, or the provider endpoint when there is no filing), and when the
/// provider was fetched. A number without provenance is not trusted, and a series built from more
/// than one provider stays inspectable.
/// </summary>
public sealed record SourceProvenance(string Provider, string? DocumentUrl, DateTimeOffset IngestedAt);
