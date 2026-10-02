namespace FinanceSentry.Modules.Auth.Infrastructure.Services;

/// <summary>Which sign-in methods the API accepts, bound from the <c>Auth</c> configuration section. Every method defaults to on.</summary>
public class AuthSignInOptions
{
    public const string SectionName = "Auth";

    public SignInMethodOptions PasswordLogin { get; set; } = new();

    public SignInMethodOptions GoogleDirect { get; set; } = new();
}

public class SignInMethodOptions
{
    public bool Enabled { get; set; } = true;
}
