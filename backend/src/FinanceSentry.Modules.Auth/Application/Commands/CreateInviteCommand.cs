using FinanceSentry.Core.Cqrs;

namespace FinanceSentry.Modules.Auth.Application.Commands;

public record CreateInviteRequest(string Email);

public record CreateInviteCommand(string Email) : ICommand<InviteDto>;

/// <summary>
/// One-time invite material. The client builds the accept link from <see cref="UserId"/> and <see cref="Token"/>;
/// there is no mailer, so the owner passes the link on by hand. The token sets the password once and stops
/// working at <see cref="ExpiresAt"/>, after use, or when a new invite is created for the same person.
/// </summary>
public record InviteDto(string UserId, string Email, string Token, DateTime ExpiresAt);
