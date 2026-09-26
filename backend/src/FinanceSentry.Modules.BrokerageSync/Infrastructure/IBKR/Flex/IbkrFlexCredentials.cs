namespace FinanceSentry.Modules.BrokerageSync.Infrastructure.IBKR.Flex;

/// <summary>Decrypted, ready-to-use Flex Web Service credential material for one user.</summary>
public sealed record IbkrFlexCredentials(Guid UserId, string Token, string QueryId);
