using System.Text.Json;
using System.Text.Json.Serialization;

namespace FinanceSentry.Modules.BrokerageSync.Infrastructure.Inzhur;

// Wire shapes of the cabinet's read endpoints, limited to the fields the cabinet UI itself reads (design report §2).
// Every member is optional: the cabinet is not a published API, so a missing field inside an asset degrades that row
// instead of failing the sync. The two top-level containers (the assets list, the broker-account balances) are
// required by InzhurApiClient: without them the answer is not a portfolio.
// Money amounts (every `…UAH` amount on an asset's details and the broker account, and a ledger row's `amount`) are
// reported in MINOR units - kopecks - exactly as the cabinet UI divides them by 100 before showing them; prices
// (`buyUAH`/`sellUAH`/`navUAH`) are in hryvnia. InzhurHoldingsMapper.FromMinorUnits is the one place that converts.
// UAH is the native currency (a currency the cabinet reports wins over that assumption); Inzhur's own USD figures are
// deliberately not read (money-semantics §3).

/// <summary><c>GET api/v1/user-assets</c>.</summary>
public sealed record InzhurUserAssetsResponse(IReadOnlyList<InzhurAsset>? Assets);

public sealed record InzhurAsset(
    JsonElement Id,
    string? Type,
    string? Status,
    string? MaturityDate,
    string? Name,
    string? Title,
    string? Isin,
    InzhurAssetPrices? Prices,
    InzhurSecurityProperties? SecurityProperties,
    InzhurAssetDetails? Details)
{
    /// <summary>The currency the cabinet reports for this asset's amounts, when it reports one.</summary>
    [JsonPropertyName("currency")]
    [JsonConverter(typeof(InzhurStringOrNullConverter))]
    public string? Currency { get; init; }

    /// <summary>Fields not modelled above; only their names are ever logged, to learn where the asset name lives.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }

    public string IdText => InzhurJson.Text(Id);
}

public sealed record InzhurAssetPrices(
    [property: JsonPropertyName("buyUAH")] decimal? BuyUah,
    [property: JsonPropertyName("sellUAH")] decimal? SellUah,
    [property: JsonPropertyName("navUAH")] decimal? NavUah);

public sealed record InzhurSecurityProperties(decimal? AvailableQuantity);

/// <summary>Amounts in kopecks (see the file header); the quantities are whole certificates.</summary>
public sealed record InzhurAssetDetails(
    [property: JsonPropertyName("certificatesOwnedQuantity")] decimal? CertificatesOwnedQuantity,
    [property: JsonPropertyName("certificatesSaleRestrictedQuantity")] decimal? CertificatesSaleRestrictedQuantity,
    [property: JsonPropertyName("totalAmountUAH")] decimal? TotalAmountUah,
    [property: JsonPropertyName("investedUAH")] decimal? InvestedUah,
    [property: JsonPropertyName("distributionsAccruedAmountUAH")] decimal? DistributionsAccruedAmountUah);

/// <summary><c>GET api/v1/users/broker-account</c>. Balances in kopecks (see the file header).</summary>
public sealed record InzhurBrokerAccount(
    [property: JsonPropertyName("availableBalanceUAH")] decimal? AvailableBalanceUah,
    [property: JsonPropertyName("blockedBalanceUAH")] decimal? BlockedBalanceUah,
    [property: JsonPropertyName("investedValueUAH")] decimal? InvestedValueUah,
    [property: JsonPropertyName("totalBalanceUAH")] decimal? TotalBalanceUah,
    [property: JsonPropertyName("bonusBalanceUAH")] decimal? BonusBalanceUah)
{
    /// <summary>The currency the cabinet reports for the account's balances, when it reports one.</summary>
    [JsonPropertyName("currency")]
    [JsonConverter(typeof(InzhurStringOrNullConverter))]
    public string? Currency { get; init; }
}

/// <summary><c>POST api/v1/auth/refresh</c>.</summary>
public sealed record InzhurRefreshResponse(string? AccessToken);

/// <summary>
/// <c>GET api/v1/assets/details?assetIds=…</c>: the asset catalog the cabinet merges into its user-asset rows, which
/// carry no name of their own.
/// </summary>
public sealed record InzhurCatalogResponse(IReadOnlyList<InzhurCatalogAsset>? Items);

public sealed record InzhurCatalogAsset(JsonElement AssetId, string? Type, string? Name, string? Isin)
{
    public string AssetIdText => InzhurJson.Text(AssetId);
}

/// <summary>
/// One row of <c>GET api/v1/users/transactions</c>, the cabinet's ledger. <see cref="Amount"/> is unsigned and in
/// kopecks; the direction comes from <see cref="Type"/>. Purchases, sales and gifts carry the certificate count in
/// <see cref="Contract"/>; distribution rows name their kind (<c>DIVIDEND</c>/<c>COUPON</c>/<c>REDEMPTION</c>).
/// </summary>
public sealed record InzhurTransaction(
    JsonElement Id,
    string? Type,
    decimal? Amount,
    string? ExternalCreatedAt,
    string? DistributionType,
    InzhurTransactionAsset? Asset,
    InzhurTransactionContract? Contract)
{
    public string IdText => InzhurJson.Text(Id);
}

public sealed record InzhurTransactionAsset(string? Type, string? Name, string? Isin);

public sealed record InzhurTransactionContract(decimal? Quantity);

/// <summary>One day's read of the cabinet.</summary>
public sealed record InzhurPortfolio(
    IReadOnlyList<InzhurAsset> Assets,
    InzhurBrokerAccount? BrokerAccount,
    IReadOnlyList<InzhurCatalogAsset>? Catalog = null);

internal static class InzhurJson
{
    /// <summary>An id the cabinet sends as a number or a string, as text; anything else is empty.</summary>
    public static string Text(JsonElement id) => id.ValueKind switch
    {
        JsonValueKind.String => id.GetString() ?? string.Empty,
        JsonValueKind.Number => id.GetRawText(),
        _ => string.Empty,
    };
}
