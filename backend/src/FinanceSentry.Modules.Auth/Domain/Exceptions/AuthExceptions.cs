using FinanceSentry.Core.Exceptions;

namespace FinanceSentry.Modules.Auth.Domain.Exceptions;

public sealed class InvalidCredentialsException()
    : ApiException(401, "INVALID_CREDENTIALS", "Invalid email or password.");

public sealed class InvalidRefreshTokenException(string message = "Refresh token invalid or expired.")
    : ApiException(401, "INVALID_REFRESH_TOKEN", message);

public sealed class GoogleAccountOnlyException()
    : ApiException(401, "GOOGLE_ACCOUNT_ONLY", "This account uses Google sign-in. Please use 'Continue with Google'.");

public sealed class DuplicateEmailException()
    : ApiException(400, "DUPLICATE_EMAIL", "Email is already registered.");

public sealed class InvalidGoogleCredentialException()
    : ApiException(400, "INVALID_GOOGLE_CREDENTIAL", "Invalid Google credential.");

public sealed class UserNotFoundException()
    : ApiException(404, "USER_NOT_FOUND", "User not found.");

public sealed class InvalidCurrentPasswordException()
    : ApiException(400, "INVALID_CURRENT_PASSWORD", "Current password is incorrect.");

public sealed class InvalidInviteException()
    : ApiException(400, "INVALID_INVITE", "This invite link is invalid or has expired. Ask the owner for a new one.");

public sealed class AccountNotInvitedException()
    : ApiException(403, "ACCOUNT_NOT_INVITED", "No account exists for this Google email. Ask the owner for an invite.");

public sealed class CannotRevokeSelfException()
    : ApiException(400, "CANNOT_REVOKE_SELF", "You cannot revoke your own access.");

public sealed class CannotRevokeOwnerException()
    : ApiException(400, "CANNOT_REVOKE_OWNER", "The owner's access cannot be revoked.");
