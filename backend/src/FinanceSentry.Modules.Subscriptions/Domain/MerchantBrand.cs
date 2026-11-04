namespace FinanceSentry.Modules.Subscriptions.Domain;

/// <summary>
/// One curated merchant: the words that name it on a statement and the single host its icon is
/// fetched from. Rows are data, not C# constants, so a merchant is added by inserting a row, the
/// way <c>detection_phrases</c> and <c>merchant_keywords</c> are. A shared reference table: not
/// per-user, no owner filter. Seeded idempotently at startup (missing rows only, so a runtime edit
/// is never clobbered), and read once per process, so an edit takes effect on the next API restart.
/// <para>
/// This table is the only source of a host the server ever calls: a statement description, a
/// merchant name or anything else a person typed picks a row, it never supplies a host.
/// </para>
/// </summary>
public class MerchantBrand
{
    public int Id { get; set; }

    /// <summary>
    /// Words that name the merchant, compared token by token against the row's merchant name (case
    /// and punctuation ignored): <c>wizz air</c> matches <c>WIZZ AIR 4567</c> but not <c>wizzard</c>.
    /// </summary>
    public string Keyword { get; set; } = string.Empty;

    /// <summary>The exact host the icon is requested from (<c>www.netflix.com</c>, not a URL): a redirect to another host is refused, so name the host that answers.</summary>
    public string Domain { get; set; } = string.Empty;
}
