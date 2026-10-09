namespace FinanceSentry.Modules.Companion.API.Requests;

using System.ComponentModel.DataAnnotations;
using FinanceSentry.Modules.Companion.Domain;

/// <summary>What the report sheet sends. Everything here is client-supplied; the server cleans it before it is stored.</summary>
public record SubmitProblemReportRequest(
    ProblemReportKind? Kind,
    [MaxLength(ProblemReportLimits.TextMaxLength)] string? Text,
    [MaxLength(ProblemReportLimits.RouteMaxLength * 4)] string? Route,
    [MaxLength(ProblemReportLimits.VersionMaxLength * 4)] string? AppVersion,
    ProblemReportDevice Device,
    [MaxLength(ProblemReportLimits.ClientPartMaxLength * 4)] string? Os,
    [MaxLength(ProblemReportLimits.ClientPartMaxLength * 4)] string? Browser);
