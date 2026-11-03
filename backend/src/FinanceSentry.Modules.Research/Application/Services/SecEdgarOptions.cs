namespace FinanceSentry.Modules.Research.Application.Services;

/// <summary>
/// SEC EDGAR client identity. SEC fair-access policy requires a descriptive User-Agent with
/// contact info; operators set <c>SecEdgar:Contact</c> (env <c>SEC_EDGAR_CONTACT</c>) to their own
/// email or URL. The committed default is the public project URL, never a personal address.
/// </summary>
public sealed class SecEdgarOptions
{
    public const string SectionName = "SecEdgar";

    public const string DefaultContact = "https://github.com/lifekit-hq/finance-sentry";

    public string Contact { get; set; } = DefaultContact;
}
