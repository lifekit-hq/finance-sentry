namespace FinanceSentry.Modules.BankSync.Application.Services;

using FinanceSentry.Core.Domain;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;

/// <summary>
/// The single definition of COMMITTED outflow. An outflow already counted in <c>Outflow</c> is
/// committed when it satisfies ANY of the rules below; everything else is discretionary.
/// <list type="bullet">
///   <item>
///     <b>(a) active commitment</b> — the key <see cref="CommitmentKeyResolver.Resolve"/>
///     derives from the debit is the key of one of the user's <c>DetectedSubscription</c> rows
///     whose status is <c>active</c>: recurring services keyed by normalized merchant name,
///     installment (розстрочка) plans keyed <c>installment:{merchant}:{roundedAmount}</c>.
///   </item>
///   <item>
///     <b>(b) committed category</b> — the debit's category is <see cref="CommittedCategories"/>
///     (<c>RENT_AND_UTILITIES</c> or <c>LOAN_PAYMENTS</c>). Rent and utilities are the largest
///     fixed outflows in the book and carry no recurrence signature the detector can see: the
///     same payee for the same amount every month is never promoted to a subscription. A loan
///     payment is a debt obligation by definition, whether or not a plan was detected for it.
///   </item>
///   <item>
///     <b>(c) counterparty obligation</b> — see <see cref="IsCommittedFlowRole"/>. Applied per
///     flow ROLE rather than per debit because counterparty transactions are excluded from the
///     per-debit pass upstream and re-enter the statistics as one synthetic row per month.
///   </item>
///   <item>
///     <b>(d) user pin</b> — the debit's
///     <see cref="MerchantNameNormalizer.NormalizeDetectionKey"/> is one of the merchant keys
///     the user pinned as committed (<c>CommittedMerchantPin</c>). This is the escape hatch for
///     obligations no rule above can see, because only the user knows they exist: a standing
///     payment to a person, a gym with a lock-in, a service billed too irregularly to detect.
///     Keyed on the detection key rather than <see cref="CommitmentKeyResolver.Resolve"/>: a pin
///     names a MERCHANT, so it must claim that merchant's charges whether or not an individual
///     row happens to look like an installment repayment.
///   </item>
/// </list>
/// <para>
/// Rules (a), (b) and (d) are per debit; rule (c) is per counterparty flow role. Consumers ask
/// this rule set rather than re-deriving a rule of their own, so every view of the split tells
/// the same story.
/// </para>
/// </summary>
public sealed class CommittedOutflowRules(
    IReadOnlySet<string> activeCommitmentKeys,
    IReadOnlySet<string> pinnedMerchantKeys)
{
    /// <summary>
    /// Categories whose spending is a standing obligation rather than a choice made this month.
    /// Deliberately narrow — <c>GENERAL_SERVICES</c> and <c>TRANSPORTATION</c>, for instance,
    /// mix subscriptions with impulse spend and would make the split meaningless.
    /// <para>
    /// <b>Known over-claim.</b> The rule inherits whatever the ingest ladder put in these two
    /// keys, and <c>RENT_AND_UTILITIES</c> is wider than rent: the telecom MCC range 4812–4900
    /// and the <c>top-up</c> keyword land there, so an ad-hoc mobile top-up reads as committed
    /// alongside the monthly phone plan. Accepted rather than carved out — a statement line
    /// carries nothing that tells a plan payment from a discretionary top-up, and narrowing by
    /// MCC here would put a second categorization opinion next to the ladder that owns it
    /// (#553). The mis-claimed amounts are small; rent, utilities and loans are not.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> CommittedCategories =
        new(StringComparer.Ordinal) { CategoryKeys.RentAndUtilities, CategoryKeys.LoanPayments };

    /// <summary>
    /// Counterparty flow roles that are committed spending. Both are standing obligations the
    /// user cannot cancel this month: <see cref="FlowRoles.FamilySupport"/> is money owed to
    /// people who depend on it, <see cref="FlowRoles.Household"/> is a household bill (the
    /// mortgage) that happens to be paid card-to-card.
    /// <para>
    /// <see cref="FlowRoles.Investment"/> and <see cref="FlowRoles.SelfRouting"/> are absent
    /// because they are not spending at all and never reach outflow. A counterparty saved with
    /// no role (<c>Counterparty.FlowRole</c> defaults to empty) IS spending but names no
    /// obligation, so it stays discretionary.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> CommittedFlowRoles =
        new(StringComparer.Ordinal) { FlowRoles.FamilySupport, FlowRoles.Household };

    private readonly IReadOnlySet<string> _activeCommitmentKeys =
        activeCommitmentKeys ?? throw new ArgumentNullException(nameof(activeCommitmentKeys));

    private readonly IReadOnlySet<string> _pinnedMerchantKeys =
        pinnedMerchantKeys ?? throw new ArgumentNullException(nameof(pinnedMerchantKeys));

    /// <summary>
    /// Rules (a), (b) and (d) for a single debit. The caller has already established that the
    /// transaction is in <c>Outflow</c> — this decides only which side of the partition it
    /// lands on, never whether it is spending.
    /// </summary>
    public bool IsCommitted(Transaction debit)
    {
        ArgumentNullException.ThrowIfNull(debit);

        // (b) first: it is a dictionary probe, while (a) and (d) have to derive a key.
        if (debit.MerchantCategory is not null && CommittedCategories.Contains(debit.MerchantCategory))
            return true;

        var commitmentKey = CommitmentKeyResolver.Resolve(
            debit.MerchantName, debit.Description, debit.Amount, debit.Mcc);

        if (_activeCommitmentKeys.Contains(commitmentKey))
            return true;

        if (_pinnedMerchantKeys.Count == 0)
            return false;

        // (d) re-derives rather than reusing commitmentKey: the resolver returns
        // installment:{merchant}:{amount} for repayment-shaped rows, which no merchant pin can
        // ever equal, so a pinned shop's repayments would slip through.
        var merchantKey = MerchantNameNormalizer.NormalizeDetectionKey(
            debit.MerchantName, debit.Description);

        return _pinnedMerchantKeys.Contains(merchantKey);
    }

    /// <summary>
    /// Rule (c): whether a counterparty flow's outbound leg is committed spending. Reads spec
    /// 044's classification directly — there is deliberately no second family-flow heuristic
    /// here, so who counts as family is decided in exactly one place.
    /// </summary>
    public bool IsCommittedFlowRole(string? flowRole) =>
        flowRole is not null && CommittedFlowRoles.Contains(flowRole);
}

/// <summary>
/// Loads the per-user data <see cref="CommittedOutflowRules"/> needs, once per request.
/// </summary>
public interface ICommittedOutflowPolicy
{
    /// <summary>
    /// The committed/discretionary rule set for a user. Loading is per user, not per
    /// transaction: the rules are set-membership tests over data that has to be read once.
    /// <para>
    /// The commitment status behind rule (a) is point-in-time — a subscription cancelled today
    /// reclassifies its past charges as discretionary too. The split describes the obligations
    /// the user holds now, not the ones they held then.
    /// </para>
    /// </summary>
    Task<CommittedOutflowRules> LoadForUserAsync(Guid userId, CancellationToken ct = default);
}

/// <inheritdoc />
public class CommittedOutflowPolicy(
    IActiveSubscriptionsReader activeSubscriptions,
    ICommittedMerchantPinRepository pins) : ICommittedOutflowPolicy
{
    private readonly IActiveSubscriptionsReader _activeSubscriptions =
        activeSubscriptions ?? throw new ArgumentNullException(nameof(activeSubscriptions));

    private readonly ICommittedMerchantPinRepository _pins =
        pins ?? throw new ArgumentNullException(nameof(pins));

    /// <inheritdoc />
    public async Task<CommittedOutflowRules> LoadForUserAsync(Guid userId, CancellationToken ct = default)
    {
        // Sequential, not Task.WhenAll: both reads sit behind scoped DbContexts, which forbid
        // concurrent operations on one instance.
        var commitmentKeys = await _activeSubscriptions.GetActiveCommitmentMerchantKeysAsync(userId, ct);
        var pinnedKeys = await _pins.GetPinnedKeysAsync(userId, ct);

        return new CommittedOutflowRules(commitmentKeys, pinnedKeys);
    }
}
