namespace FinanceSentry.Modules.BrokerageSync.Domain.Exceptions;

/// <summary>
/// A stored broker credential could not be decrypted or parsed (a rotated or lost key, a corrupt PEM): only
/// connecting again with fresh credentials can fix it.
/// </summary>
public sealed class BrokerCredentialUnreadableException(string brokerName, Exception innerException)
    : InvalidOperationException($"The stored {brokerName} credential could not be read.", innerException);
