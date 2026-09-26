using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.IBKR.Flex;

namespace FinanceSentry.Modules.BrokerageSync.Application.Services;

/// <summary>
/// Converts raw Flex Web Service wire DTOs (<see cref="FlexTradeXml"/>, <see cref="FlexCashTransactionXml"/>)
/// into domain entities. Every money field is copied verbatim in its native currency — see
/// <see cref="BrokerageTrade"/>'s doc comment for the conversion-at-the-reader-boundary rule.
/// </summary>
public static class IbkrFlexMapper
{
    private const string DateFormat = "yyyyMMdd";
    private const string TimeFormat = "HHmmss";

    public static BrokerageTrade CreateTrade(
        Guid userId, string provider, FlexTradeXml xml, Guid? instrumentId)
    {
        return new BrokerageTrade(
            userId,
            provider,
            xml.IbExecutionId ?? string.Empty,
            xml.TradeId ?? string.Empty,
            ParseConid(xml.Conid),
            NullIfEmpty(xml.Isin),
            xml.Symbol ?? string.Empty,
            NullIfEmpty(xml.OpenCloseIndicator),
            ParseCompoundDateTime(xml.OpenDateTime),
            ParseDateTime(xml.TradeDate, xml.TradeTime),
            ParseDecimal(xml.Quantity) ?? 0m,
            ParseDecimal(xml.TradePrice) ?? 0m,
            ParseDecimal(xml.Proceeds) ?? 0m,
            ParseDecimal(xml.CostBasis),
            ParseDecimal(xml.RealizedPnl),
            ParseDecimal(xml.IbCommission),
            NullIfEmpty(xml.IbCommissionCurrency),
            ParseDecimal(xml.Taxes),
            xml.Currency ?? string.Empty,
            ParseDecimal(xml.FxRateToBase),
            instrumentId);
    }

    public static void RefreshTrade(BrokerageTrade trade, FlexTradeXml xml, Guid? instrumentId)
    {
        trade.Refresh(
            ParseConid(xml.Conid),
            NullIfEmpty(xml.Isin),
            xml.Symbol ?? string.Empty,
            NullIfEmpty(xml.OpenCloseIndicator),
            ParseCompoundDateTime(xml.OpenDateTime),
            ParseDateTime(xml.TradeDate, xml.TradeTime),
            ParseDecimal(xml.Quantity) ?? 0m,
            ParseDecimal(xml.TradePrice) ?? 0m,
            ParseDecimal(xml.Proceeds) ?? 0m,
            ParseDecimal(xml.CostBasis),
            ParseDecimal(xml.RealizedPnl),
            ParseDecimal(xml.IbCommission),
            NullIfEmpty(xml.IbCommissionCurrency),
            ParseDecimal(xml.Taxes),
            xml.Currency ?? string.Empty,
            ParseDecimal(xml.FxRateToBase),
            instrumentId);
    }

    public static BrokerageCashTransaction CreateCashTransaction(Guid userId, string provider, FlexCashTransactionXml xml)
    {
        return new BrokerageCashTransaction(
            userId,
            provider,
            ComputeCashTransactionIdempotencyKey(xml),
            ParseConid(xml.Conid),
            NullIfEmpty(xml.Isin),
            NullIfEmpty(xml.Symbol),
            ParseCompoundDateTime(xml.DateTime) ?? default,
            ParseDecimal(xml.Amount) ?? 0m,
            xml.Type ?? string.Empty,
            NullIfEmpty(xml.Code),
            ParseDecimal(xml.Section871mWithholding),
            xml.Currency ?? string.Empty,
            ParseDecimal(xml.FxRateToBase));
    }

    /// <summary>
    /// IBKR gives cash transaction rows no stable id of their own, so the idempotency key is a
    /// deterministic hash of the fields that together identify a unique row on the statement:
    /// account-scoped conid, date/time, amount, type and code. A re-pull of an overlapping
    /// window reproduces the same key for the same row.
    /// </summary>
    public static string ComputeCashTransactionIdempotencyKey(FlexCashTransactionXml xml)
    {
        var raw = string.Join(
            '|',
            xml.AccountId,
            xml.Conid,
            xml.DateTime,
            xml.Amount,
            xml.Type,
            xml.Code,
            xml.TradeId);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash);
    }

    public static long? ParseConid(string? value)
    {
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    private static decimal? ParseDecimal(string? value)
    {
        return decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    private static DateTime ParseDateTime(string? date, string? time)
    {
        if (string.IsNullOrWhiteSpace(date))
            return default;

        if (string.IsNullOrWhiteSpace(time))
            return DateTime.SpecifyKind(DateTime.ParseExact(date, DateFormat, CultureInfo.InvariantCulture), DateTimeKind.Utc);

        var combined = $"{date};{time}";
        return ParseCompoundDateTime(combined) ?? default;
    }

    /// <summary>Parses IBKR's compound <c>"yyyyMMdd;HHmmss"</c> wire format, used for
    /// <c>openDateTime</c> and cash transaction <c>dateTime</c>. Returns <c>null</c> for an
    /// empty or missing value.</summary>
    private static DateTime? ParseCompoundDateTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var format = value.Contains(';') ? $"{DateFormat};{TimeFormat}" : DateFormat;
        return DateTime.TryParseExact(
            value, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result)
            ? DateTime.SpecifyKind(result, DateTimeKind.Utc)
            : null;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
