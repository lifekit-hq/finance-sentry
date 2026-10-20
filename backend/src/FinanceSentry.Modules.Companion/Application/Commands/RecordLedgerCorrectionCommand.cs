namespace FinanceSentry.Modules.Companion.Application.Commands;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Companion.Domain;
using FinanceSentry.Modules.Companion.Domain.Repositories;

/// <summary>
/// Records one correction the owner made to Ledger. Returns the new row's id, or null when nothing was written: a blank
/// or over-long field, or a correction time in the future. The caller is told, never thrown at. A missing time means now.
/// </summary>
public sealed record RecordLedgerCorrectionCommand(
    Guid UserId,
    string AgentSaid,
    string WhatWasWrong,
    string OwnerCorrection,
    DateTimeOffset? CorrectedAt) : ICommand<long?>;

public sealed class RecordLedgerCorrectionCommandHandler(ILedgerCorrectionRepository corrections)
    : ICommandHandler<RecordLedgerCorrectionCommand, long?>
{
    public async Task<long?> Handle(RecordLedgerCorrectionCommand command, CancellationToken ct)
    {
        var agentSaid = Clean(command.AgentSaid, LedgerCorrectionLimits.AgentSaidMaxLength);
        var whatWasWrong = Clean(command.WhatWasWrong, LedgerCorrectionLimits.WhatWasWrongMaxLength);
        var ownerCorrection = Clean(command.OwnerCorrection, LedgerCorrectionLimits.OwnerCorrectionMaxLength);
        if (agentSaid is null || whatWasWrong is null || ownerCorrection is null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var correctedAt = command.CorrectedAt ?? now;
        if (correctedAt > now + LedgerCorrectionLimits.MaxClockSkew)
        {
            return null;
        }

        var saved = await corrections.AddAsync(new LedgerCorrection
        {
            UserId = command.UserId,
            AgentSaid = agentSaid,
            WhatWasWrong = whatWasWrong,
            OwnerCorrection = ownerCorrection,
            CorrectedAt = correctedAt.ToUniversalTime(),
            RecordedAt = now,
        }, ct);

        return saved.Id;
    }

    private static string? Clean(string? value, int maxLength)
    {
        var text = value?.Trim() ?? string.Empty;
        return text.Length == 0 || text.Length > maxLength ? null : text;
    }
}
