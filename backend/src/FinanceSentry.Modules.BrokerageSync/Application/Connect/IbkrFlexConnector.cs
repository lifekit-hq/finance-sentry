using System.Globalization;
using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.IBKR.Flex;
using Microsoft.Extensions.Logging;

namespace FinanceSentry.Modules.BrokerageSync.Application.Connect;

public sealed class IbkrFlexConnector(
    IIBKRFlexCredentialRepository credentialRepository,
    ICredentialEncryptionService encryption,
    IIbkrFlexClient flexClient,
    ILogger<IbkrFlexConnector> logger) : IIbkrFlexConnector
{
    private const string WireDateFormat = "yyyyMMdd";
    private const string WireDateTimeFormat = "yyyyMMdd;HHmmss";
    private const string BaseSummaryLevel = "BASE_SUMMARY";
    private const string LotLevel = "LOT";

    public async Task ConnectAsync(Guid userId, ConnectIbkrFlexArtifacts artifacts, CancellationToken ct)
    {
        var existing = await credentialRepository.GetByUserIdAsync(userId, ct);
        var token = encryption.Encrypt(artifacts.Token);

        if (existing is not null)
        {
            existing.Replace(artifacts.QueryId, token.Ciphertext, token.Iv, token.AuthTag, token.KeyVersion);
            credentialRepository.Update(existing);
        }
        else
        {
            var credential = new IBKRFlexCredential(
                userId, artifacts.QueryId, token.Ciphertext, token.Iv, token.AuthTag, token.KeyVersion);
            await credentialRepository.AddAsync(credential, ct);
        }

        await credentialRepository.SaveChangesAsync(ct);
        logger.LogInformation("IBKR Flex credential persisted for user {UserId}", userId);
    }

    public async Task<IbkrFlexPreview> PreviewAsync(Guid userId, ConnectIbkrFlexArtifacts artifacts, CancellationToken ct)
    {
        var statement = await flexClient.FetchStatementAsync(
            new IbkrFlexCredentials(userId, artifacts.Token, artifacts.QueryId), ct: ct);

        var cashCurrencies = statement.CashReport
            .Where(r => !string.Equals(r.LevelOfDetail, BaseSummaryLevel, StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(r.Currency))
            .Select(r => r.Currency!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToList();

        return new IbkrFlexPreview(
            statement.AccountId,
            ParseDate(statement.FromDate),
            ParseDate(statement.ToDate),
            ParseGeneratedAt(statement.WhenGenerated),
            statement.OpenPositions.Count(p => !string.Equals(p.LevelOfDetail, LotLevel, StringComparison.OrdinalIgnoreCase)),
            cashCurrencies,
            statement.Trades.Count,
            statement.CashTransactions.Count);
    }

    private static DateOnly? ParseDate(string value) =>
        DateOnly.TryParseExact(value, WireDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    private static DateTime? ParseGeneratedAt(string? value) =>
        DateTime.TryParseExact(
            value, WireDateTimeFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var generated)
            ? generated
            : null;
}
