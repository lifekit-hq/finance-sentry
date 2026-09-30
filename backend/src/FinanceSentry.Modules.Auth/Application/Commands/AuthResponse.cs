namespace FinanceSentry.Modules.Auth.Application.Commands;

public record UserDto(string Id, string Email, IReadOnlyList<string> Roles);

public record AuthResponse(UserDto User, DateTime ExpiresAt);

public record MeResponse(UserDto User, DateTime ExpiresAt, UserProfileDto Profile);
