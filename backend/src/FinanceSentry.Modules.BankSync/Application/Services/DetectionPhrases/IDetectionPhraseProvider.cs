namespace FinanceSentry.Modules.BankSync.Application.Services.DetectionPhrases;

/// <summary>The current <see cref="DetectionPhraseSet"/>, read from the <c>detection_phrases</c> table.</summary>
public interface IDetectionPhraseProvider
{
    DetectionPhraseSet Current { get; }
}

/// <summary>A provider over a fixed set. For callers that have no database: tests and tools.</summary>
public sealed class FixedDetectionPhraseProvider(DetectionPhraseSet set) : IDetectionPhraseProvider
{
    public static FixedDetectionPhraseProvider Seed { get; } = new(DetectionPhraseSet.Seed);

    public DetectionPhraseSet Current { get; } = set ?? throw new ArgumentNullException(nameof(set));
}
