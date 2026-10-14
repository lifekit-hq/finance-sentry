using FinanceSentry.Core.Cqrs;

namespace FinanceSentry.Modules.Auth.Application.Commands;

/// <summary>An Owner reopens a closed account before its data is purged.</summary>
public record ReopenAccountCommand(string UserId, Guid RequestedByUserId) : ICommand<Unit>;
