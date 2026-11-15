namespace FinanceSentry.Core.Interfaces;

/// <summary>One single-turn completion: a system prompt, one user message, no tools, no streaming.</summary>
/// <param name="ModelId">The model to ask for; null uses the host's configured model.</param>
public sealed record MemoModelRequest(string System, string User, int MaxOutputTokens, string? ModelId = null);

/// <summary>What the model said and what the call consumed, so a caller can price it.</summary>
public sealed record MemoModelReply(string Text, string ModelId, int InputTokens, int OutputTokens);

/// <summary>
/// Core port to the one model call of the discovery funnel (the research memo on a coverage entrant). Research
/// cannot reference the Agent module that holds the Anthropic credentials, so the Agent module implements this and
/// Research takes it optionally: where no implementation is registered, or <see cref="IsConfigured"/> is false, the
/// memo step does nothing and nothing is spent. Same shape as <see cref="ICoverageUniverseReader"/>.
/// </summary>
public interface IMemoLanguageModel
{
    /// <summary>False when no API key is configured; the caller must not spend a call then.</summary>
    bool IsConfigured { get; }

    /// <summary>Runs the completion. Throws when the provider answers with an error; the caller owns the retry policy.</summary>
    Task<MemoModelReply> CompleteAsync(MemoModelRequest request, CancellationToken ct = default);
}
