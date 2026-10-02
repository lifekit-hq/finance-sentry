using FinanceSentry.Core.Cqrs;

namespace FinanceSentry.Modules.Auth.Application.Commands;

/// <summary>
/// A sign-in already proven by an external identity provider. <paramref name="Subject"/> is the provider's stable
/// id for the person; <paramref name="EmailVerified"/> says whether the provider vouches for <paramref name="Email"/>.
/// </summary>
public record ExternalLoginCommand(string Provider, string Subject, string Email, bool EmailVerified) : ICommand<AuthResult>;
