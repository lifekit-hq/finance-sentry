using System.Text.Json.Serialization;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Wealth.Application.Queries;

namespace FinanceSentry.Mcp.Responses;

/// <summary>
/// What <c>record_risk_remeasurement</c> returns: the recorded statement version and its diff exactly as before (#700), plus
/// the capacity indicator's SUGGESTED band read at the moment of recording (#454 S7), so the owner sees it beside the number
/// they gave. <see cref="CapacityIndicator"/> is omitted when the profile could not be read. The indicator never wrote
/// anything: the recorded numbers are the ones the owner said.
/// </summary>
public sealed record RiskRemeasurementToolResult(
    IpsDto Ips,
    IpsDiffDto Diff,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CapacitySuggestion? CapacityIndicator);
