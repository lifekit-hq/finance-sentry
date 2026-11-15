namespace FinanceSentry.Modules.Wealth.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Wealth.Domain.Repositories;

/// <summary>The sentence every tax-profile read carries: what is on file is the person's own statement, not advice.</summary>
public static class TaxProfileText
{
    public const string Disclosure =
        "The person's own statement about their tax situation, kept as context. It is not tax advice and nothing is computed "
        + "from it: surface it, say what it does not cover, and refer the person to a qualified tax adviser for anything definitive.";
}

/// <summary>
/// The tax profile as the person stated it. Every value is null until set (a person with no profile gets an empty one, with
/// <paramref name="UpdatedAt"/> null). <paramref name="JurisdictionNotes"/> and <paramref name="Caveat"/> are null when the
/// host cannot read the encrypted fields (<paramref name="Readable"/> false); <paramref name="HasJurisdictionNotes"/> and
/// <paramref name="HasCaveat"/> still say whether the person wrote them.
/// </summary>
public record TaxProfileDto(
    bool Readable,
    string? Jurisdiction,
    string? JurisdictionNotes,
    string? Caveat,
    bool HasJurisdictionNotes,
    bool HasCaveat,
    DateTimeOffset? UpdatedAt,
    string Disclosure = TaxProfileText.Disclosure)
{
    /// <summary>True when the person has set nothing.</summary>
    public bool IsEmpty => Jurisdiction is null && !HasJurisdictionNotes && !HasCaveat;
}

public record GetTaxProfileQuery(Guid UserId) : IQuery<TaxProfileDto>;

/// <summary>
/// On a host that holds no encryption key (the MCP host) it reads only the plain columns and the null tests on the
/// encrypted ones, and returns the profile without its free text - the smallest read that needs no key.
/// </summary>
public class GetTaxProfileQueryHandler(ITaxProfileRepository profiles) : IQueryHandler<GetTaxProfileQuery, TaxProfileDto>
{
    public async Task<TaxProfileDto> Handle(GetTaxProfileQuery query, CancellationToken cancellationToken)
    {
        if (!profiles.CanReadEncryptedFields)
        {
            return await profiles.GetOutlineAsync(query.UserId, cancellationToken) is { } outline
                ? new TaxProfileDto(false, outline.Jurisdiction, null, null, outline.HasJurisdictionNotes, outline.HasCaveat, outline.UpdatedAt)
                : Empty(false);
        }

        return await profiles.GetAsync(query.UserId, cancellationToken) is { } p
            ? new TaxProfileDto(
                true, p.Jurisdiction, p.JurisdictionNotes, p.Caveat, p.JurisdictionNotes is not null, p.Caveat is not null, p.UpdatedAt)
            : Empty(true);
    }

    private static TaxProfileDto Empty(bool readable) => new(readable, null, null, null, false, false, null);
}
