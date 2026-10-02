namespace FinanceSentry.Modules.Auth.Application.Commands;

/// <summary>
/// The signed-in user. <see cref="Permissions"/> is the effective permission set (role plus per-person
/// grants); the frontend uses it to hide navigation and guard routes, while the API enforces it per request.
/// </summary>
public record UserDto(string Id, string Email, IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions);

public record AuthResponse(UserDto User, DateTime ExpiresAt);

public record MeResponse(UserDto User, DateTime ExpiresAt, UserProfileDto Profile);

/// <summary>The sign-in methods a deployment offers; the login page renders only these.</summary>
public record SignInMethodsResponse(bool Oidc, bool PasswordLogin, bool GoogleDirect);
