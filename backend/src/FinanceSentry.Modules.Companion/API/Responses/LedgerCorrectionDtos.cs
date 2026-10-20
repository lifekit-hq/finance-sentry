namespace FinanceSentry.Modules.Companion.API.Responses;

/// <summary>One month of the Ledger correction log, plus the count for every month that has corrections.</summary>
public sealed record LedgerCorrectionsResult(
    string Month,
    int Count,
    IReadOnlyList<LedgerCorrectionItem> Items,
    IReadOnlyList<LedgerCorrectionMonth> ByMonth);

public sealed record LedgerCorrectionItem(
    long Id,
    string AgentSaid,
    string WhatWasWrong,
    string OwnerCorrection,
    DateTimeOffset CorrectedAt,
    DateTimeOffset RecordedAt);

public sealed record LedgerCorrectionMonth(string Month, int Count);
