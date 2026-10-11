using FinanceSentry.Core.Cqrs;

namespace FinanceSentry.Modules.Auth.Application.Commands;

public record CreateInviteRequest(string Email);

public record CreateInviteCommand(string Email) : ICommand<InviteDto>;

/// <summary>
/// An invited person: a passwordless account that the first identity-provider sign-in with the same verified
/// email links to. There is no mailer and no invite link, so the owner tells the person to sign in.
/// </summary>
public record InviteDto(string UserId, string Email);
