using System.Text.Json;
using System.Text.Json.Serialization;

namespace FinanceSentry.Modules.BrokerageSync.Infrastructure.Inzhur;

// Wire shapes of the cabinet's read endpoints, limited to the fields the cabinet UI itself reads (design report §2).
// Every member is optional: the cabinet is not a published API, so a missing field degrades a row instead of failing
// the sync. UAH amounts are the native ones; Inzhur's own USD figures are deliberately not read (money-semantics §3).

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
    /// <summary>Fields not modelled above; only their names are ever logged, to learn where the asset name lives.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }

    public string IdText => Id.ValueKind switch
    {
        JsonValueKind.String => Id.GetString() ?? string.Empty,
        JsonValueKind.Number => Id.GetRawText(),
        _ => string.Empty,
    };
}

public sealed record InzhurAssetPrices(
    [property: JsonPropertyName("buyUAH")] decimal? BuyUah,
    [property: JsonPropertyName("sellUAH")] decimal? SellUah,
    [property: JsonPropertyName("navUAH")] decimal? NavUah);

public sealed record InzhurSecurityProperties(decimal? AvailableQuantity);

public sealed record InzhurAssetDetails(
    [property: JsonPropertyName("certificatesOwnedQuantity")] decimal? CertificatesOwnedQuantity,
    [property: JsonPropertyName("certificatesSaleRestrictedQuantity")] decimal? CertificatesSaleRestrictedQuantity,
    [property: JsonPropertyName("totalAmountUAH")] decimal? TotalAmountUah,
    [property: JsonPropertyName("investedUAH")] decimal? InvestedUah,
    [property: JsonPropertyName("distributionsAccruedAmountUAH")] decimal? DistributionsAccruedAmountUah);

/// <summary><c>GET api/v1/users/broker-account</c>.</summary>
public sealed record InzhurBrokerAccount(
    [property: JsonPropertyName("availableBalanceUAH")] decimal? AvailableBalanceUah,
    [property: JsonPropertyName("blockedBalanceUAH")] decimal? BlockedBalanceUah,
    [property: JsonPropertyName("investedValueUAH")] decimal? InvestedValueUah,
    [property: JsonPropertyName("totalBalanceUAH")] decimal? TotalBalanceUah,
    [property: JsonPropertyName("bonusBalanceUAH")] decimal? BonusBalanceUah);

/// <summary><c>POST api/v1/auth/refresh</c>.</summary>
public sealed record InzhurRefreshResponse(string? AccessToken);

/// <summary>One day's read of the cabinet.</summary>
public sealed record InzhurPortfolio(IReadOnlyList<InzhurAsset> Assets, InzhurBrokerAccount? BrokerAccount);
