namespace FinanceSentry.Modules.Auth.Application.Commands;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Auth.Application.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Exceptions;
using FinanceSentry.Modules.Auth.Infrastructure.Services;
using Microsoft.Extensions.Options;

/// <summary>Direct Google sign-in: verifies the Google credential, then signs in through the provider-agnostic external login.</summary>
public class VerifyGoogleCredentialCommandHandler(
    IGoogleCredentialVerifier verifier,
    ICommandHandler<ExternalLoginCommand, AuthResult> externalLogin,
    IOptions<AuthSignInOptions> signIn) : ICommandHandler<VerifyGoogleCredentialCommand, AuthResult>
{
    public const string LoginProvider = "Google";

    public async Task<AuthResult> Handle(VerifyGoogleCredentialCommand request, CancellationToken cancellationToken)
    {
        if (!signIn.Value.GoogleDirect.Enabled)
            throw new SignInMethodDisabledException();

        var googleUser = await verifier.VerifyAsync(request.Credential);

        // The verifier rejects credentials whose email Google has not verified, so a verified one reaches here.
        return await externalLogin.Handle(
            new ExternalLoginCommand(LoginProvider, googleUser.GoogleId, googleUser.Email, EmailVerified: true),
            cancellationToken);
    }
}
