namespace FinanceSentry.Modules.Companion.Application.Services;

using FinanceSentry.Core.Utils;

/// <summary>
/// Turns an event's relative <see cref="Domain.CompanionEvent.AppPath"/> into the absolute link the agent wake and the
/// MCP event carry (the agent has no app host of its own). Null whenever either half is missing or unusable, so a
/// consumer omits the link instead of printing a relative or wrong one.
/// </summary>
public static class CompanionAppUrl
{
    public static string? For(string? publicBaseUrl, string? appPath) => AppUrl.For(publicBaseUrl, appPath);
}
