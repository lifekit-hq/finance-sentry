using FinanceSentry.Core.Exceptions;

namespace FinanceSentry.Modules.CryptoSync.Domain.Exceptions;

/// <summary>
/// Base for venue API and credential failures. Defaults to 422 / INVALID_CREDENTIALS — a venue
/// that rejects a call is, from the user's side, a credential that does not work. Each venue
/// derives its own type so logs name the venue.
/// </summary>
public abstract class CryptoExchangeException : ApiException
{
    protected CryptoExchangeException(string message)
        : base(422, "INVALID_CREDENTIALS", message)
    {
    }

    protected CryptoExchangeException(string message, Exception innerException)
        : base(422, "INVALID_CREDENTIALS", message, innerException)
    {
    }
}

public sealed class ExchangeAlreadyConnectedException(string provider)
    : ApiException(409, "ALREADY_CONNECTED",
        $"A {CryptoExchangeProvider.DisplayName(provider)} account is already connected for this user.");

public sealed class ExchangeAccountNotFoundException(string provider)
    : ApiException(404, "NOT_FOUND",
        $"No {CryptoExchangeProvider.DisplayName(provider)} account is connected for this user.");

/// <summary>A sync for a provider no adapter is registered for — a wiring bug, not user input.</summary>
public sealed class UnknownExchangeProviderException(string provider)
    : ApiException(404, "NOT_FOUND", $"Unknown crypto exchange provider '{provider}'.");
