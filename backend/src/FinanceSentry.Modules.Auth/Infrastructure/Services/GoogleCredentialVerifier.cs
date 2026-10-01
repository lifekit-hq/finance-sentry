using Google.Apis.Auth;
using FinanceSentry.Modules.Auth.Application.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Exceptions;
using Microsoft.Extensions.Options;

namespace FinanceSentry.Modules.Auth.Infrastructure.Services;

public class GoogleCredentialVerifier(IOptions<GoogleOAuthOptions> options) : IGoogleCredentialVerifier
{
    private readonly string clientId = options.Value.ClientId;

    public async Task<GoogleUserInfo> VerifyAsync(string credential)
    {
        try
        {
            var payload = await GoogleJsonWebSignature.ValidateAsync(
                credential,
                new GoogleJsonWebSignature.ValidationSettings { Audience = new[] { clientId } });
            if (!payload.EmailVerified)
                throw new InvalidGoogleCredentialException();
            return new GoogleUserInfo(payload.Subject, payload.Email, payload.Name);
        }
        catch (Exception)
        {
            throw new InvalidGoogleCredentialException();
        }
    }
}
