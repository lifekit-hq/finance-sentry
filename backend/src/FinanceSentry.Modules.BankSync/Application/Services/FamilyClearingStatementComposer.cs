namespace FinanceSentry.Modules.BankSync.Application.Services;

using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.BankSync.Application.Queries;

/// <summary>The Telegram-bound monthly statement: <c>Headline</c> is the alert title, <c>Body</c> the message.</summary>
public sealed record FamilyStatementBrief(string Headline, string Body);

/// <summary>
/// Composes the monthly family-clearing digest (#434 S5) from a <see cref="FamilyClearingStatement"/>.
/// Pure — no I/O, no clock. Mirrors <c>PerformanceBriefComposer</c>'s line-budget shape so both
/// deliveries respect the same Ledger message-format rule.
/// </summary>
public static class FamilyClearingStatementComposer
{
    /// <summary>Ledger message-format rule: the delivered message, headline included, stays ≤12 lines.</summary>
    private const int MaxMessageLines = 12;

    /// <summary>Totals line + (optional) excluded-legs line, reserved ahead of the counterparty lines.</summary>
    private const int ReservedFooterLines = 1;

    private const decimal ThousandThreshold = 1_000m;
    private const decimal MillionThreshold = 1_000_000m;

    public static FamilyStatementBrief Compose(FamilyClearingStatement statement, string? appBaseUrl = null)
    {
        var headline = BuildHeadline(statement);
        var lines = new List<string>();

        var hasExcluded = statement.ExcludedRoutingLegs > 0;
        var footerLines = ReservedFooterLines + (hasExcluded ? 1 : 0);
        var budget = Math.Max(0, MaxMessageLines - 1 - footerLines);

        if (statement.Counterparties.Count == 0)
        {
            lines.Add(AppUrl.Bullet("No family-support activity this month.", appBaseUrl, null));
        }
        else
        {
            // Reserve one more slot for the truncation line itself when the list won't all fit.
            var willTruncate = statement.Counterparties.Count > budget;
            var take = willTruncate ? Math.Max(0, budget - 1) : budget;

            var shown = statement.Counterparties.Take(take).ToList();
            // A counterparty has no page of its own, so its bullet stays unlinked.
            lines.AddRange(shown.Select(c => AppUrl.Bullet(BuildCounterpartyLine(c), appBaseUrl, null)));

            var remaining = statement.Counterparties.Count - shown.Count;
            if (remaining > 0)
            {
                lines.Add(AppUrl.Bullet($"+{remaining} more — ask for detail.", appBaseUrl, null));
            }
        }

        lines.Add(AppUrl.Bullet(
            $"Total: {FormatUsd(statement.ReceivedTotalUsd)} in, {FormatUsd(statement.SupportTotalUsd)} out",
            appBaseUrl,
            AlertAppPath.AccountsList));

        if (hasExcluded)
        {
            lines.Add(AppUrl.Bullet(
                $"{statement.ExcludedRoutingLegs} routing leg(s) excluded — not missing money.", appBaseUrl, null));
        }

        return new FamilyStatementBrief(headline, string.Join("\n", lines));
    }

    private static string BuildHeadline(FamilyClearingStatement statement)
        => $"Family statement — {statement.Month}: {FormatUsd(statement.ReceivedTotalUsd)} in, " +
           $"{FormatUsd(statement.SupportTotalUsd)} out";

    private static string BuildCounterpartyLine(CounterpartyStatementLine line)
    {
        var rentSuffix = line.RentConfirmed switch
        {
            true => " — rent confirmed",
            false => " — rent short",
            null => string.Empty,
        };

        return $"{line.Name}: recv {FormatUsd(line.ReceivedUsd)}, sent {FormatUsd(line.SentUsd)}{rentSuffix}";
    }

    private static string FormatUsd(decimal usd)
    {
        var abs = Math.Abs(usd);
        return abs >= MillionThreshold ? $"${usd / MillionThreshold:0.#}m"
            : abs >= ThousandThreshold ? $"${usd / ThousandThreshold:0.#}k"
            : $"${usd:0}";
    }
}
