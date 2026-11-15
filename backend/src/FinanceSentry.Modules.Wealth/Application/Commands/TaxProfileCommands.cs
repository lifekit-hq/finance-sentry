namespace FinanceSentry.Modules.Wealth.Application.Commands;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Wealth.Application.Queries;
using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Domain.Repositories;

/// <summary>
/// The tax profile as the person sent it. Every field is optional; a request restates the whole profile, so an omitted or
/// blank field clears that value. The validator refuses a request with none of them (use DELETE to remove the profile).
/// </summary>
public record TaxProfileInput(string? Jurisdiction, string? JurisdictionNotes, string? Caveat);

public record UpsertTaxProfileCommand(Guid UserId, TaxProfileInput Input) : ICommand<TaxProfileDto>;

public record DeleteTaxProfileCommand(Guid UserId) : ICommand<Unit>;

// Each write is put on the audit trail by the context's AuditedWriteSaveChangesInterceptor (field names, never values);
// the handlers do not remember to.
public class UpsertTaxProfileCommandHandler(ITaxProfileRepository profiles)
    : ICommandHandler<UpsertTaxProfileCommand, TaxProfileDto>
{
    public async Task<TaxProfileDto> Handle(UpsertTaxProfileCommand command, CancellationToken cancellationToken)
    {
        var i = command.Input;
        var profile = await profiles.GetAsync(command.UserId, cancellationToken);
        if (profile is null)
        {
            profile = TaxProfile.Create(command.UserId, i.Jurisdiction, i.JurisdictionNotes, i.Caveat);
            await profiles.AddAsync(profile, cancellationToken);
        }
        else
        {
            profile.Update(i.Jurisdiction, i.JurisdictionNotes, i.Caveat);
            await profiles.SaveAsync(cancellationToken);
        }

        return new TaxProfileDto(
            true, profile.Jurisdiction, profile.JurisdictionNotes, profile.Caveat, profile.JurisdictionNotes is not null,
            profile.Caveat is not null, profile.UpdatedAt);
    }
}

/// <summary>Idempotent: deleting a profile that is not there is not an error.</summary>
public class DeleteTaxProfileCommandHandler(ITaxProfileRepository profiles)
    : ICommandHandler<DeleteTaxProfileCommand, Unit>
{
    public async Task<Unit> Handle(DeleteTaxProfileCommand command, CancellationToken cancellationToken)
    {
        if (await profiles.GetAsync(command.UserId, cancellationToken) is { } profile)
            await profiles.RemoveAsync(profile, cancellationToken);

        return Unit.Value;
    }
}
