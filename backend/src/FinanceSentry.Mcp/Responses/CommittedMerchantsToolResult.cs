using System.Text.Json.Serialization;
using FinanceSentry.Modules.BankSync.Application.Queries;

namespace FinanceSentry.Mcp.Responses;

/// <summary>
/// Union-shaped result for the merged <c>committed_merchants</c> tool (spec 554, rule (d)). Only
/// the members relevant to the requested action are populated; null members are omitted from the
/// JSON so each action returns a clean shape. Reuses the BankSync module's
/// <see cref="CommittedMerchantPinDto"/> — no second pin shape.
/// </summary>
public sealed record CommittedMerchantsToolResult
{
    /// <summary>Echoes the requested action: <c>list</c> | <c>pin</c> | <c>unpin</c>.</summary>
    public required string Action { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CommittedMerchantPinDto>? Pins { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CommittedMerchantPinDto? Pin { get; init; }

    /// <summary>True when <c>pin</c> found the merchant already pinned and stored nothing.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AlreadyPinned { get; init; }

    /// <summary>False when <c>unpin</c> found no pin for that merchant.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Unpinned { get; init; }

    /// <summary>Populated when the call was malformed (e.g. <c>pin</c> without a merchant).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; init; }

    public static CommittedMerchantsToolResult ForList(IReadOnlyList<CommittedMerchantPinDto> pins)
        => new() { Action = "list", Pins = pins };

    public static CommittedMerchantsToolResult ForPin(CommittedMerchantPinDto pin, bool alreadyPinned)
        => new() { Action = "pin", Pin = pin, AlreadyPinned = alreadyPinned };

    public static CommittedMerchantsToolResult ForUnpin(bool unpinned)
        => new() { Action = "unpin", Unpinned = unpinned };

    public static CommittedMerchantsToolResult Invalid(string action, string reason)
        => new() { Action = action, Error = reason };
}
