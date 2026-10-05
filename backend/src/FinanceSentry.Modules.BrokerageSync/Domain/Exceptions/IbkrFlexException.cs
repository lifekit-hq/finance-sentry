using FinanceSentry.Core.Exceptions;

namespace FinanceSentry.Modules.BrokerageSync.Domain.Exceptions;

/// <summary>An IBKR Flex Web Service call failed — a rejected token/query id, an expired
/// reference code, or any other <c>FlexStatementResponse</c> error. The API error code names
/// the actionable cases (bad token, expired token, unknown query, still generating) so the
/// connect form can say what to fix; anything else stays <c>IBKR_FLEX_ERROR</c>.</summary>
public sealed class IbkrFlexException(string? flexErrorCode, string message)
    : ApiException(422, MapErrorCode(flexErrorCode), message)
{
    private static readonly Dictionary<string, string> ErrorCodes = new()
    {
        ["1015"] = "IBKR_FLEX_INVALID_TOKEN",
        ["1012"] = "IBKR_FLEX_TOKEN_EXPIRED",
        ["1013"] = "IBKR_FLEX_IP_RESTRICTED",
        ["1014"] = "IBKR_FLEX_QUERY_NOT_FOUND",
        ["1020"] = "IBKR_FLEX_QUERY_NOT_FOUND",
        ["1001"] = "IBKR_FLEX_NOT_READY",
        ["1009"] = "IBKR_FLEX_NOT_READY",
        ["1019"] = "IBKR_FLEX_NOT_READY",
        ["1018"] = "IBKR_FLEX_RATE_LIMITED",
    };

    /// <summary>IBKR's own numeric error code (e.g. <c>"1003"</c>), when the venue provided one.</summary>
    public string? FlexErrorCode { get; } = flexErrorCode;

    private static string MapErrorCode(string? flexErrorCode) =>
        flexErrorCode is not null && ErrorCodes.TryGetValue(flexErrorCode, out var code) ? code : "IBKR_FLEX_ERROR";
}
