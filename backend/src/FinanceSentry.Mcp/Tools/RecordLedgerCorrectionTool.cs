using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Companion.Application.Commands;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

public sealed record RecordLedgerCorrectionResult(bool Recorded, long? Id);

[McpServerToolType]
public sealed class RecordLedgerCorrectionTool(
    ICommandHandler<RecordLedgerCorrectionCommand, long?> handler,
    IIdentityResolver identity)
{
    [McpServerTool(Name = "record_ledger_correction")]
    [Description("Records one correction the user made to you (Ledger) in the correction log: the user told you something you said was wrong - a fact, number, date, verdict or reading of their book, or that you did not do what they asked. Call it once per correction, before you reply with the fixed answer. A new instruction or a preference that does not say you were wrong is not a correction. A human reviews the log to improve your instructions and tests; nothing is fed back to you automatically, and recording a correction never changes data, money or orders. Plain text fields: agentSaid at most 2000 characters, whatWasWrong at most 1000, ownerCorrection at most 2000. Returns recorded=false and no id for a blank or over-long field or a correctedAt in the future.")]
    public async Task<RecordLedgerCorrectionResult> ExecuteAsync(
        [Description("What you said that the user corrected, quoted or tightly paraphrased. Max 2000 characters.")] string agentSaid,
        [Description("What was wrong with it, in your own words (wrong fact, stale number, wrong date, ignored instruction, ...). Max 1000 characters.")] string whatWasWrong,
        [Description("The user's correction, quoted or tightly paraphrased. Max 2000 characters.")] string ownerCorrection,
        [Description("When the user made the correction (ISO-8601). Omit to use now.")] DateTimeOffset? correctedAt = null,
        CancellationToken cancellationToken = default)
    {
        var effective = identity.GetUserId();
        if (effective is null)
        {
            return new RecordLedgerCorrectionResult(false, null);
        }

        var id = await handler.Handle(
            new RecordLedgerCorrectionCommand(effective.Value, agentSaid, whatWasWrong, ownerCorrection, correctedAt),
            cancellationToken);
        return new RecordLedgerCorrectionResult(id is not null, id);
    }
}
