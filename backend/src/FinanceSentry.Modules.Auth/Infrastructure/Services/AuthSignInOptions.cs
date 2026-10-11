namespace FinanceSentry.Modules.Auth.Infrastructure.Services;

/// <summary>Which sign-in methods the API accepts, bound from the <c>Auth</c> configuration section.</summary>
public class AuthSignInOptions
{
    public const string SectionName = "Auth";

    /// <summary>Break-glass only: an operator signing in with an existing account password when the identity provider is down.</summary>
    public SignInMethodOptions PasswordLogin { get; set; } = new();
}

public class SignInMethodOptions
{
    /// <summary>Off unless an operator turns it on (<c>Auth__PasswordLogin__Enabled</c>); with it off the password endpoint does not exist.</summary>
    public bool Enabled { get; set; }
}
