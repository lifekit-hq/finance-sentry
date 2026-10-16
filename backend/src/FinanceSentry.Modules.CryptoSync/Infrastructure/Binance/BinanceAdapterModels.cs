using System.Text.Json;
using System.Text.Json.Serialization;

namespace FinanceSentry.Modules.CryptoSync.Infrastructure.Binance;

public sealed record BinanceAccountResponse(
    [property: JsonPropertyName("balances")] IReadOnlyList<BinanceBalance> Balances);

public sealed record BinanceBalance(
    [property: JsonPropertyName("asset")] string Asset,
    [property: JsonPropertyName("free")] string Free,
    [property: JsonPropertyName("locked")] string Locked);

public sealed record BinancePriceTicker(
    [property: JsonPropertyName("symbol")] string Symbol,
    [property: JsonPropertyName("price")] string Price);

public sealed record BinanceErrorResponse(
    [property: JsonPropertyName("code")] int Code,
    [property: JsonPropertyName("msg")] string Message);

// /sapi/v1/asset/get-funding-asset — flat list of per-asset balances in the Funding wallet.
public sealed record BinanceFundingAsset(
    [property: JsonPropertyName("asset")] string Asset,
    [property: JsonPropertyName("free")] string Free,
    [property: JsonPropertyName("locked")] string? Locked,
    [property: JsonPropertyName("freeze")] string? Freeze,
    [property: JsonPropertyName("withdrawing")] string? Withdrawing);

// Generic paginated wrapper for the Simple Earn endpoints.
public sealed record BinanceEarnPage<T>(
    [property: JsonPropertyName("rows")] IReadOnlyList<T> Rows,
    [property: JsonPropertyName("total")] int Total);

// /sapi/v1/simple-earn/flexible/position — Simple Earn Flexible product positions.
public sealed record BinanceFlexibleEarnPosition(
    [property: JsonPropertyName("asset")] string Asset,
    [property: JsonPropertyName("totalAmount")] string TotalAmount);

// /sapi/v1/simple-earn/locked/position — Simple Earn Locked product positions.
public sealed record BinanceLockedEarnPosition(
    [property: JsonPropertyName("asset")] string Asset,
    [property: JsonPropertyName("amount")] string Amount);

// /api/v3/myTrades — per-symbol trade history (one row per fill).
public sealed record BinanceTradeRow(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("symbol")] string Symbol,
    [property: JsonPropertyName("price")] string Price,
    [property: JsonPropertyName("qty")] string Quantity,
    [property: JsonPropertyName("quoteQty")] string QuoteQuantity,
    [property: JsonPropertyName("commission")] string Commission,
    [property: JsonPropertyName("commissionAsset")] string CommissionAsset,
    [property: JsonPropertyName("time")] long TimeMs,
    [property: JsonPropertyName("isBuyer")] bool IsBuyer);

// /sapi/v1/fiat/orders and /sapi/v1/fiat/payments share one envelope; code "000000" is success.
public sealed record BinanceFiatPage<T>(
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("data")] IReadOnlyList<T>? Data,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("success")] bool Success);

// /sapi/v1/fiat/orders — a fiat deposit (transactionType 0) or withdrawal (1). indicatedAmount is the trade amount,
// amount what was credited or paid out after totalFee.
public sealed record BinanceFiatOrder(
    [property: JsonPropertyName("orderNo")] string OrderNo,
    [property: JsonPropertyName("fiatCurrency")] string? FiatCurrency,
    [property: JsonPropertyName("indicatedAmount")] string? IndicatedAmount,
    [property: JsonPropertyName("amount")] string? Amount,
    [property: JsonPropertyName("totalFee")] string? TotalFee,
    [property: JsonPropertyName("method")] string? Method,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("createTime")] long CreateTimeMs,
    [property: JsonPropertyName("updateTime")] long? UpdateTimeMs);

// /sapi/v1/fiat/payments — crypto bought (transactionType 0) or sold (1) for fiat: sourceAmount of fiatCurrency
// for obtainAmount of cryptoCurrency. paymentMethod is reported on buys only.
public sealed record BinanceFiatPayment(
    [property: JsonPropertyName("orderNo")] string OrderNo,
    [property: JsonPropertyName("sourceAmount")] string? SourceAmount,
    [property: JsonPropertyName("fiatCurrency")] string? FiatCurrency,
    [property: JsonPropertyName("obtainAmount")] string? ObtainAmount,
    [property: JsonPropertyName("cryptoCurrency")] string? CryptoCurrency,
    [property: JsonPropertyName("totalFee")] string? TotalFee,
    [property: JsonPropertyName("price")] string? Price,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("paymentMethod")] string? PaymentMethod,
    [property: JsonPropertyName("createTime")] long CreateTimeMs,
    [property: JsonPropertyName("updateTime")] long? UpdateTimeMs);

// /sapi/v1/capital/deposit/hisrec — a coin deposit. status: 0 pending, 6 credited but cannot withdraw, 7 wrong
// deposit, 8 waiting user confirm, 1 success, 2 rejected. transferType 0 is on-chain, 1 from another Binance account.
// insertTime is when the deposit was seen, completeTime when it was credited (absent on older rows).
public sealed record BinanceCoinDeposit(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("amount")] string? Amount,
    [property: JsonPropertyName("coin")] string? Coin,
    [property: JsonPropertyName("network")] string? Network,
    [property: JsonPropertyName("status")] int Status,
    [property: JsonPropertyName("txId")] string? TxId,
    [property: JsonPropertyName("insertTime")] long InsertTimeMs,
    [property: JsonPropertyName("completeTime")] long? CompleteTimeMs,
    [property: JsonPropertyName("transferType")] int TransferType);

// /sapi/v1/capital/withdraw/history — a coin withdrawal. status: 0 email sent, 2 awaiting approval, 3 rejected,
// 4 processing, 6 completed. amount is what was sent, transactionFee the network fee charged on top (unverified, see
// BinanceAdapter.GetChainFlowsAsync). applyTime and completeTime are UTC "yyyy-MM-dd HH:mm:ss" strings.
public sealed record BinanceCoinWithdrawal(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("amount")] string? Amount,
    [property: JsonPropertyName("transactionFee")] string? TransactionFee,
    [property: JsonPropertyName("coin")] string? Coin,
    [property: JsonPropertyName("network")] string? Network,
    [property: JsonPropertyName("status")] int Status,
    [property: JsonPropertyName("txId")] string? TxId,
    [property: JsonPropertyName("applyTime")] string? ApplyTime,
    [property: JsonPropertyName("completeTime")] string? CompleteTime,
    [property: JsonPropertyName("transferType")] int TransferType);

// /sapi/v1/simple-earn/flexible/history/rewardsRecord — one payout of a flexible product (type BONUS, REALTIME or REWARDS).
public sealed record BinanceFlexibleReward(
    [property: JsonPropertyName("asset")] string? Asset,
    [property: JsonPropertyName("rewards")] string? Rewards,
    [property: JsonPropertyName("projectId")] string? ProjectId,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("time")] long TimeMs);

// /sapi/v1/simple-earn/locked/history/rewardsRecord — one payout of a locked position.
public sealed record BinanceLockedReward(
    [property: JsonPropertyName("positionId"), JsonConverter(typeof(BinanceStringOrNumberConverter))] string? PositionId,
    [property: JsonPropertyName("asset")] string? Asset,
    [property: JsonPropertyName("amount")] string? Amount,
    [property: JsonPropertyName("time")] long TimeMs);

// /sapi/v1/asset/assetDividend — a distribution or airdrop; enInfo is free text and there is no documented type list.
public sealed record BinanceAssetDividendPage(
    [property: JsonPropertyName("rows")] IReadOnlyList<BinanceAssetDividend>? Rows,
    [property: JsonPropertyName("total")] int Total);

public sealed record BinanceAssetDividend(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("tranId")] long? TranId,
    [property: JsonPropertyName("asset")] string? Asset,
    [property: JsonPropertyName("amount")] string? Amount,
    [property: JsonPropertyName("enInfo")] string? Info,
    [property: JsonPropertyName("divTime")] long TimeMs);

// /sapi/v1/asset/dribblet — dust conversions to BNB: totalTransferedAmount received after totalServiceChargeAmount.
public sealed record BinanceDribbletResponse(
    [property: JsonPropertyName("userAssetDribblets")] IReadOnlyList<BinanceDribblet>? Dribblets);

public sealed record BinanceDribblet(
    [property: JsonPropertyName("transId")] long TransId,
    [property: JsonPropertyName("operateTime")] long OperateTimeMs,
    [property: JsonPropertyName("totalTransferedAmount")] string? TransferedAmount,
    [property: JsonPropertyName("totalServiceChargeAmount")] string? ServiceChargeAmount);

/// <summary>Reads a JSON string or number as its text, so an id Binance sends as a number keeps the digits a string id would carry.</summary>
public sealed class BinanceStringOrNumberConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.Number => JsonElement.ParseValue(ref reader).GetRawText(),
            _ => reader.GetString(),
        };

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options) => writer.WriteStringValue(value);
}
