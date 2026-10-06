namespace FinanceSentry.Core.Connections;

/// <summary>
/// Reads one provider's exception as a <see cref="ProviderFailure"/> (report §5). Each provider adapter
/// ships its own; the policy never looks at an exception.
/// </summary>
public interface IProviderFailureClassifier
{
    /// <summary>The provider this classifier reads, as the sync paths name it (<c>monobank</c>, <c>ibkr</c>, …).</summary>
    string Provider { get; }

    ProviderFailure Classify(Exception exception, ProviderFailureContext context);
}

/// <summary>What a classifier may need besides the exception.</summary>
/// <param name="At">When the attempt failed.</param>
/// <param name="Current">The connection's health before this attempt.</param>
public sealed record ProviderFailureContext(DateTimeOffset At, ConnectionHealth Current);
