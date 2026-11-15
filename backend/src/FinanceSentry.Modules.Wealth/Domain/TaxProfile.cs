namespace FinanceSentry.Modules.Wealth.Domain;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Domain;
using FinanceSentry.Infrastructure.Auditing;

/// <summary>
/// The tax situation the person states about themselves (#454): which jurisdiction's rules they live under, what they want
/// remembered about it, and a caveat in their own words. One row per person. It is context the agent surfaces and refers on
/// ("you told me X; check it with a tax adviser"), never a rule engine: nothing here is computed from, and the disposal-tax
/// estimate (<c>get_disposal_tax_context</c>) does not read it.
/// <para>
/// <see cref="JurisdictionNotes"/> and <see cref="Caveat"/> are free text and field-encrypted (the columns hold
/// ciphertext; the properties are plaintext in code); every save is put on the audit trail by field name. The
/// <see cref="Jurisdiction"/> code is a shape any planner would hold, so it stays readable and a host without the key can
/// still say which jurisdiction is on file. Every value is optional and a blank one is stored as null, never as an empty
/// string. Not stored: tax identifiers (PPSN), returns, revenue correspondence.
/// </para>
/// </summary>
[AuditedProfileData(ResourceTypeName)]
public sealed class TaxProfile : IHasUpdatedAt, IUserOwned
{
    /// <summary>The resource type the audit trail records for a write of this entity.</summary>
    public const string ResourceTypeName = "tax_profile";

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }

    /// <summary>ISO 3166-1 alpha-2 code, upper case ("IE"); null when the person gave none.</summary>
    public string? Jurisdiction { get; private set; }

    /// <summary>Free text about the jurisdiction's rules as the person understands them; encrypted at rest.</summary>
    public string? JurisdictionNotes { get; private set; }

    /// <summary>Free text the person wants read beside every tax figure; encrypted at rest.</summary>
    public string? Caveat { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private TaxProfile() { }

    public static TaxProfile Create(Guid userId, string? jurisdiction, string? jurisdictionNotes, string? caveat)
    {
        var profile = new TaxProfile
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        profile.Update(jurisdiction, jurisdictionNotes, caveat);
        return profile;
    }

    /// <summary>Restates the profile: an omitted or blank value clears that field (it is not left as it was).</summary>
    public void Update(string? jurisdiction, string? jurisdictionNotes, string? caveat)
    {
        Jurisdiction = Normalize(jurisdiction)?.ToUpperInvariant();
        JurisdictionNotes = Normalize(jurisdictionNotes);
        Caveat = Normalize(caveat);
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>The bounds a tax profile is validated against and stored in.</summary>
public static class TaxProfileLimits
{
    public const int JurisdictionLength = 2;
    public const int TextMaxLength = 2_000;
}
