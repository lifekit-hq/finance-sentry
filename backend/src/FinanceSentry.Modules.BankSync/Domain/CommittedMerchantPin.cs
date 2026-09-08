namespace FinanceSentry.Modules.BankSync.Domain;

using FinanceSentry.Core.Domain;

/// <summary>
/// A merchant the user declared a standing obligation — rule (d) of
/// <c>CommittedOutflowRules</c>. It exists for the commitments only the user knows about: a
/// standing payment to a person, a gym with a lock-in period, a service billed too irregularly
/// for the recurrence detector to promote it.
/// </summary>
public class CommittedMerchantPin : Entity
{
    /// <summary>Owning user. Pins are always personal — there is no system-default pin.</summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// The pin's identity: the key produced by
    /// <c>MerchantNameNormalizer.NormalizeDetectionKey</c>. Storing the normalized key rather
    /// than the typed name is what lets one pin claim every spelling the statement uses for the
    /// merchant, which is the same reason the recurrence detector keys its rows this way.
    /// </summary>
    public string MerchantKey { get; set; } = string.Empty;

    /// <summary>
    /// What the user typed, kept for display only. The key is lowercased and stripped, so
    /// echoing it back would show "mario scalas" where the user wrote "Mario Scalas".
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;
}
