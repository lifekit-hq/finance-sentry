using FinanceSentry.Core.Cqrs;

namespace FinanceSentry.Modules.Auth.Application.Commands;

/// <summary>
/// A sign-in already proven by an external identity provider. <paramref name="Subject"/> is the provider's stable
/// id for the person; <paramref name="EmailVerified"/> says whether the provider vouches for <paramref name="Email"/>;
/// <paramref name="HasAppRole"/> says whether the provider grants the person this app's role, which lets a first
/// sign-in with no account create one.
/// </summary>
public record ExternalLoginCommand(string Provider, string Subject, string Email, bool EmailVerified, bool HasAppRole = false)
    : ICommand<AuthResult>;
