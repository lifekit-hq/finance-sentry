namespace FinanceSentry.Core.Connections;

/// <summary>The policy's verdict on one attempt: the health to store, and what the caller should do about it.</summary>
public sealed record ConnectionHealthEvaluation(
    ConnectionHealth Previous,
    ConnectionHealth Health,
    ConnectionHealthOutcome Outcome);
