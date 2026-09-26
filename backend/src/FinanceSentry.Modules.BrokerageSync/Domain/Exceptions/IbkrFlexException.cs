using FinanceSentry.Core.Exceptions;

namespace FinanceSentry.Modules.BrokerageSync.Domain.Exceptions;

/// <summary>An IBKR Flex Web Service call failed — a rejected token/query id, an expired
/// reference code, or any other <c>FlexStatementResponse</c> error other than "not yet generated".</summary>
public sealed class IbkrFlexException(string? flexErrorCode, string message)
    : ApiException(422, "IBKR_FLEX_ERROR", message)
{
    /// <summary>IBKR's own numeric error code (e.g. <c>"1003"</c>), when the venue provided one.</summary>
    public string? FlexErrorCode { get; } = flexErrorCode;
}
