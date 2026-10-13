using FinanceSentry.Core.Cqrs;

namespace FinanceSentry.Modules.Auth.Application.Commands;

public record GetPreferencesQuery(Guid UserId) : IQuery<PreferencesDto>;
