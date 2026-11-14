namespace FinanceSentry.Modules.Research.Application.Services;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FinanceSentry.Modules.Research.API.Responses;

/// <summary>
/// Turns a dossier into (a) the agent prompt for "Ledger's read" and (b) a fingerprint of the facts
/// that read was based on (feature 421, US3).
/// <para>The fingerprint is the data-change half of cache invalidation: it covers only material
/// facts, never <c>GeneratedAt</c>-style timestamps that move on every request, so an unchanged
/// dossier keeps producing an unchanged digest.</para>
/// </summary>
public static class LedgerReadComposer
{
    private const int PromptNewsLimit = 5;
    private const int PromptActionLimit = 5;
    private const int PromptSignalLimit = 5;

    public static string Fingerprint(AssetDossierResult dossier)
    {
        var sb = new StringBuilder();
        sb.Append(dossier.Symbol);

        if (dossier.Position is { } p)
        {
            sb.Append("|pos:").Append(p.Provider).Append(':')
                .Append(Num(p.Quantity)).Append(':')
                .Append(Num(p.CurrentValueUsd)).Append(':')
                .Append(Num(p.CostBasisUsd)).Append(':')
                .Append(p.TaxLots.Count);
        }

        if (dossier.Thesis is { } t)
        {
            // UpdatedAt/BrokenAt move only when the thesis itself changes.
            sb.Append("|thesis:").Append(t.Id).Append(':')
                .Append(t.UpdatedAt.ToUnixTimeSeconds()).Append(':')
                .Append(t.BrokenAt?.ToUnixTimeSeconds() ?? 0);
        }

        if (dossier.Valuation is { } v)
        {
            sb.Append("|val:").Append(v.NotApplicable).Append(':')
                .Append(Num(v.Price)).Append(':')
                .Append(Num(v.ConsensusTarget)).Append(':')
                .Append(Num(v.ImpliedUpsidePct));
        }

        if (dossier.Analysts is { } a)
        {
            sb.Append("|analysts:").Append(a.Coverage).Append(':')
                .Append(a.RecentActions.Count).Append(':')
                .Append(a.RecentActions.Count > 0 ? a.RecentActions[0].ActionDate.DayNumber : 0).Append(':')
                .Append(a.Trends.Count > 0 ? a.Trends[0].Period.DayNumber : 0);
        }

        sb.Append("|news:").Append(dossier.RecentNews.Count);
        foreach (var n in dossier.RecentNews)
        {
            sb.Append(':').Append(n.Id);
        }

        if (dossier.NextEarnings is { } e)
        {
            sb.Append("|earnings:").Append(e.EventDate.DayNumber).Append(':').Append(e.IsEstimate);
        }

        sb.Append("|signals:").Append(dossier.RadarSignals.Count);
        foreach (var s in dossier.RadarSignals)
        {
            sb.Append(':').Append(s.Timestamp.ToUnixTimeSeconds()).Append(s.SignalType);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }

    public static string Prompt(AssetDossierResult d)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            $"Write a concise read on {d.Symbol} for the portfolio owner — what you see, what matters now, " +
            "and what would change your mind. Ground every claim in the facts below and say so plainly " +
            "when a section is missing rather than guessing. Under 200 words, prose, no headings.");
        sb.AppendLine();
        sb.AppendLine($"## {d.Symbol} — facts on file");

        if (d.Position is { } p)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"Position ({p.Provider}): {Num(p.Quantity)} units worth ${Num(p.CurrentValueUsd)}");
            if (p.CostBasisUsd is not null)
            {
                sb.Append(CultureInfo.InvariantCulture,
                    $", cost basis ${Num(p.CostBasisUsd)}, unrealised ${Num(p.UnrealizedPnlUsd)} ({Num(p.UnrealizedPnlPercent)}%)");
            }

            sb.AppendLine($", {p.TaxLots.Count} tax lot(s).");
        }
        else
        {
            sb.AppendLine("Position: not held.");
        }

        if (d.Thesis is { } t)
        {
            var status = t.BrokenAt is null ? "active" : $"broken ({t.BrokenReason})";
            sb.AppendLine($"Thesis ({status}): {t.ThesisText}");
            if (t.InvalidationTriggers.Count > 0)
            {
                sb.AppendLine("Invalidation triggers: " +
                    string.Join("; ", t.InvalidationTriggers.Select(x => x.ToString())));
            }

            if (t.NarrativeFalsifiers is { Count: > 0 } narrativeFalsifiers)
            {
                sb.AppendLine("Narrative falsifiers (model-checked, not monitored): " +
                    string.Join("; ", narrativeFalsifiers));
            }
        }
        else
        {
            sb.AppendLine("Thesis: none on file.");
        }

        if (d.Valuation is { NotApplicable: false } v)
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"Valuation: price ${Num(v.Price)}, consensus target ${Num(v.ConsensusTarget)}, implied upside {Num(v.ImpliedUpsidePct)}%{(v.IsStale ? " (stale)" : string.Empty)}.");
        }

        if (d.Analysts is { } a && a.RecentActions.Count > 0)
        {
            sb.AppendLine("Recent analyst actions:");
            foreach (var act in a.RecentActions.Take(PromptActionLimit))
            {
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"- {act.ActionDate:yyyy-MM-dd} {act.Firm}: {act.ActionType} {act.PriorRating}->{act.NewRating}, target {Num(act.PriorTarget)}->{Num(act.NewTarget)}");
            }
        }

        if (d.NextEarnings is { } e)
        {
            sb.AppendLine($"Next earnings: {e.EventDate:yyyy-MM-dd}{(e.IsEstimate ? " (estimated)" : string.Empty)}.");
        }

        if (d.RadarSignals.Count > 0)
        {
            sb.AppendLine("Recent radar signals:");
            foreach (var s in d.RadarSignals.Take(PromptSignalLimit))
            {
                sb.AppendLine($"- {s.Timestamp:yyyy-MM-dd} {s.Scanner}/{s.SignalType} ({s.Severity})");
            }
        }

        if (d.RecentNews.Count > 0)
        {
            sb.AppendLine("Recent news:");
            foreach (var n in d.RecentNews.Take(PromptNewsLimit))
            {
                sb.AppendLine($"- {n.PublishedAt:yyyy-MM-dd} [{n.Source}] {n.Title}");
            }
        }

        return sb.ToString();
    }

    private static string Num(decimal? value) =>
        value?.ToString("0.####", CultureInfo.InvariantCulture) ?? "n/a";
}
