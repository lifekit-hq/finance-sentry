namespace FinanceSentry.Modules.BankSync.Application.Services.DetectionPhrases;

/// <summary>One detection phrase as the matching code reads it. The persisted shape is <c>Domain.DetectionPhrase</c>.</summary>
public sealed record DetectionPhraseEntry(
    string Kind,
    string Phrase,
    string? Provider = null,
    string? Locale = null,
    int? MccCondition = null,
    string? CategoryCondition = null);
