namespace FinanceSentry.Modules.BankSync.Application.Commands;

using FinanceSentry.Core.Auditing;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Domain;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Application.Services.CategoryMapping;
using FinanceSentry.Modules.BankSync.Domain.Repositories;

public enum RecategorizeOutcome
{
    /// <summary>The transaction now carries the requested category.</summary>
    Changed,

    /// <summary>It already carried it; nothing was written.</summary>
    Unchanged,

    /// <summary>No active transaction of the caller has that id.</summary>
    NotFound,

    /// <summary>The requested category is not in the canonical list.</summary>
    UnknownCategory,

    /// <summary>The move would change what the transaction is in the money flow (see <see cref="RecategorizeTransactionCommandHandler.AffectsMoneyFigures"/>).</summary>
    FlowCategory,
}

/// <param name="PreviousCategory">What the transaction was filed under; PUT it back to undo the change.</param>
public sealed record RecategorizeTransactionResult(RecategorizeOutcome Outcome, string? PreviousCategory, string? Category);

/// <summary>
/// Files one transaction under another spending category. Reversible by construction: the result names the previous
/// category, and the same command with it puts the transaction back. The change is recorded on the audit trail as
/// <paramref name="Actor"/> (ids and the field name, never a value) before it commits.
/// <para>
/// Not durable: nothing marks the row as a manual choice, so a later operator recategorise pass
/// (<c>RecategorizationCommand</c> via <c>TransactionRecategorizationService.ReResolveFromStoredSignal</c>) re-resolves
/// every row from its stored signal and can silently put the previous category back, with no audit entry. PUT again
/// to redo the change.
/// </para>
/// </summary>
public sealed record RecategorizeTransactionCommand(Guid UserId, Guid TransactionId, string Category, AuditActorKind Actor)
    : ICommand<RecategorizeTransactionResult>;

public sealed class RecategorizeTransactionCommandHandler(
    ITransactionRepository transactions,
    ICategoryReadService categories,
    IAuditTrail auditTrail)
    : ICommandHandler<RecategorizeTransactionCommand, RecategorizeTransactionResult>
{
    /// <summary>
    /// The categories that decide how a transaction counts in the money flow: income, transfers (never spend or
    /// cash-flow), loan payments and rent-and-utilities (the committed outflow categories,
    /// <c>CommittedOutflowPolicy</c>). Moving a transaction into or out of one changes money figures, not only a label,
    /// so it is not a reversible recategorisation and is refused.
    /// </summary>
    public static readonly IReadOnlySet<string> FlowCategories = new HashSet<string>(StringComparer.Ordinal)
    {
        CategoryKeys.Income,
        CategoryKeys.TransferIn,
        CategoryKeys.TransferOut,
        CategoryKeys.LoanPayments,
        CategoryKeys.RentAndUtilities,
    };

    /// <summary>
    /// Whether the move changes a money figure: either end is a flow category, or it crosses the edge of the day-to-day
    /// spend set the coverage sentinel reads (<see cref="CoverageGapDetector.DailyCategories"/>); a move inside that set
    /// leaves its total as it was.
    /// </summary>
    public static bool AffectsMoneyFigures(string? previous, string category)
        => FlowCategories.Contains(category)
           || (previous is not null && FlowCategories.Contains(previous))
           || (previous is not null && CoverageGapDetector.DailyCategories.Contains(previous))
              != CoverageGapDetector.DailyCategories.Contains(category);

    private const string ResourceType = "transaction";
    private const string FieldName = "MerchantCategory";

    public async Task<RecategorizeTransactionResult> Handle(RecategorizeTransactionCommand command, CancellationToken ct)
    {
        var transaction = await transactions.GetByIdAsync(command.TransactionId, ct);
        if (transaction is null || !transaction.IsActive || transaction.UserId != command.UserId)
            return new RecategorizeTransactionResult(RecategorizeOutcome.NotFound, null, null);

        var previous = transaction.MerchantCategory;
        var requested = command.Category?.Trim() ?? string.Empty;

        var known = await categories.GetAllAsync(ct);
        var category = known.FirstOrDefault(c => string.Equals(c.Key, requested, StringComparison.Ordinal))?.Key;
        if (category is null)
            return new RecategorizeTransactionResult(RecategorizeOutcome.UnknownCategory, previous, null);

        if (AffectsMoneyFigures(previous, category))
            return new RecategorizeTransactionResult(RecategorizeOutcome.FlowCategory, previous, category);

        if (string.Equals(previous ?? CategoryKeys.Uncategorized, category, StringComparison.Ordinal))
            return new RecategorizeTransactionResult(RecategorizeOutcome.Unchanged, previous, category);

        await auditTrail.RecordAsync(
            new AuditEntry(command.UserId, command.UserId, command.Actor, AuditAction.Update, ResourceType, transaction.Id, [FieldName]),
            ct);

        transaction.MerchantCategory = category;
        await transactions.SaveChangesAsync(ct);

        return new RecategorizeTransactionResult(RecategorizeOutcome.Changed, previous, category);
    }
}
