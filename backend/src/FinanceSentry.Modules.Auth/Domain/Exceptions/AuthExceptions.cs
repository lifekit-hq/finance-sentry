using FinanceSentry.Core.Exceptions;

namespace FinanceSentry.Modules.Auth.Domain.Exceptions;

public sealed class InvalidCredentialsException()
    : ApiException(401, "INVALID_CREDENTIALS", "Invalid email or password.");

public sealed class AccountLockedException : ApiException
{
    public AccountLockedException(TimeSpan remaining)
        : base(429, "ACCOUNT_LOCKED", Describe(remaining))
    {
        RetryAfterSeconds = (int)Math.Ceiling(Math.Max(remaining.TotalSeconds, 1));
    }

    private static string Describe(TimeSpan remaining)
    {
        var minutes = Math.Max((int)Math.Ceiling(remaining.TotalMinutes), 1);
        return $"Too many failed sign-in attempts. Try again in {minutes} {(minutes == 1 ? "minute" : "minutes")}.";
    }
}

public sealed class SignInMethodDisabledException()
    : ApiException(403, "SIGN_IN_METHOD_DISABLED", "This sign-in method is not available.");

public sealed class InvalidRefreshTokenException(string message = "Refresh token invalid or expired.")
    : ApiException(401, "INVALID_REFRESH_TOKEN", message);

public sealed class ExternalAccountOnlyException()
    : ApiException(401, "EXTERNAL_ACCOUNT_ONLY", "This account signs in through single sign-on and has no password.");

public sealed class DuplicateEmailException()
    : ApiException(400, "DUPLICATE_EMAIL", "Email is already registered.");

public sealed class UserNotFoundException()
    : ApiException(404, "USER_NOT_FOUND", "User not found.");

public sealed class AccountUpdateConflictException()
    : ApiException(409, "ACCOUNT_UPDATE_CONFLICT", "Your account was changed elsewhere. Reload and try again.");

public sealed class AccountNotInvitedException()
    : ApiException(403, "ACCOUNT_NOT_INVITED", "No account exists for this email. Ask the owner for an invite.");

public sealed class CannotRevokeSelfException()
    : ApiException(400, "CANNOT_REVOKE_SELF", "You cannot revoke your own access.");

public sealed class CannotRevokeOwnerException()
    : ApiException(400, "CANNOT_REVOKE_OWNER", "The owner's access cannot be revoked.");

public sealed class ClosureConfirmationMismatchException()
    : ApiException(400, "CLOSURE_CONFIRMATION_MISMATCH", "Type your account email exactly to confirm closing the account.");

public sealed class LastOwnerCannotCloseException()
    : ApiException(409, "LAST_OWNER_CANNOT_CLOSE", "You are the only owner. Make someone else an owner before closing your account.");

public sealed class AccountNotClosedException()
    : ApiException(409, "ACCOUNT_NOT_CLOSED", "This account is not closed and waiting to be deleted, or its access was revoked.");
