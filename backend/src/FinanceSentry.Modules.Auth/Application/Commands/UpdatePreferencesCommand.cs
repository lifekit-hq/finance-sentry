using FinanceSentry.Core.Cqrs;

namespace FinanceSentry.Modules.Auth.Application.Commands;

public record UpdatePreferencesCommand(Guid UserId, AppearanceDto? Appearance) : ICommand<PreferencesDto>;
