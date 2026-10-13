namespace FinanceSentry.Modules.Research.Application.Commands;

using System.Globalization;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Ports;
using FinanceSentry.Modules.Research.Domain.Recommendations;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.Extensions.Logging;

/// <summary>
/// The rule sweep (#1069): for every user with a risk rule set or a thesis, a held name whose thesis broke gets an Exit
/// proposal, and a held name over the single-position cap (not silenced by an acknowledgement) gets a Trim back to the
/// cap. It never replaces a pending proposal, and it does not re-propose a trigger it already put (since the thesis broke,
/// or in the last 30 days for the cap). Proposals are queued silently - nothing is pushed. Runs with no person in
/// scope, so every read is job-safe.
/// </summary>
public sealed record RunRecommendationTriggersCommand : ICommand<int>;

public sealed class RunRecommendationTriggersCommandHandler(
    IPositionCapSource capSource,
    IThesisRepository theses,
    IRecommendationRepository repo,
    IPolicyAckReader policyAcks,
    IBookFiguresService bookFigures,
    RecommendationPositionReader positions,
    RecommendationWriter writer,
    ILogger<RunRecommendationTriggersCommandHandler> logger)
    : ICommandHandler<RunRecommendationTriggersCommand, int>
{
    public const int CapCooldownDays = 30;
    public const int ExpiryDays = 14;
    private const decimal Percent = 100m;

    public async Task<int> Handle(RunRecommendationTriggersCommand command, CancellationToken ct)
    {
        var userIds = (await capSource.ListUserIdsAsync(ct))
            .Union(await theses.GetUserIdsWithThesesUnscopedAsync(ct))
            .ToList();

        var created = 0;
        foreach (var userId in userIds)
        {
            try
            {
                created += await SweepUserAsync(userId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Recommendation sweep failed for user {UserId}", userId);
            }
        }

        return created;
    }

    private async Task<int> SweepUserAsync(Guid userId, CancellationToken ct)
    {
        var book = await bookFigures.ReadAsync(userId, ct);
        if (book.TotalValueUsd <= 0m)
        {
            return 0;
        }

        var cap = await capSource.GetMaxPositionWeightAsync(userId, ct);
        var userTheses = await theses.ListUnscopedAsync(userId, ct);
        bool? capSilenced = null;
        var created = 0;

        var held = book.Positions
            .GroupBy(p => p.Symbol.ToUpperInvariant())
            .Select(g => (Ticker: g.Key, Usd: g.Sum(p => p.UsdValue)))
            .Where(h => h.Usd > 0m)
            .OrderByDescending(h => h.Usd);

        foreach (var (ticker, usd) in held)
        {
            if (await repo.FindPendingUnscopedAsync(userId, ticker, ct) is not null)
            {
                continue;
            }

            var tickerTheses = userTheses.Where(t => t.Ticker.Equals(ticker, StringComparison.OrdinalIgnoreCase)).ToList();
            var broken = tickerTheses.Where(t => t.BrokenAt is not null).OrderByDescending(t => t.BrokenAt).FirstOrDefault();
            if (broken is not null)
            {
                if (await TryProposeExitAsync(userId, book, ticker, broken, ct))
                {
                    created++;
                }

                continue;
            }

            if (RecommendationSizing.ExcessOverCapUsd(usd, book.TotalValueUsd, cap) is null)
            {
                continue;
            }

            capSilenced ??= await policyAcks.IsPolicySilencedAsync(userId, RecommendationEvidenceFactory.MaxPositionWeightRule, ct);
            if (capSilenced.Value)
            {
                continue;
            }

            if (await TryProposeTrimAsync(userId, book, ticker, usd, cap!.Value, tickerTheses.FirstOrDefault(), ct))
            {
                created++;
            }
        }

        return created;
    }

    private async Task<bool> TryProposeExitAsync(
        Guid userId, BookFigures book, string ticker, InvestmentThesis broken, CancellationToken ct)
    {
        var triggerRef = broken.Id.ToString();
        if (await repo.ExistsForTriggerSinceUnscopedAsync(
            userId, ticker, RecommendationTriggerKind.ThesisBreak, triggerRef, broken.BrokenAt!.Value, ct))
        {
            return false;
        }

        var position = await positions.ReadAsync(book, ticker, ct);
        if (position is not { Held: true })
        {
            return false;
        }

        var reason = broken.BrokenReason ?? "an invalidation trigger fired";
        var recommendation = NewRuleProposal(userId, ticker, RecommendationAction.Exit, RecommendationTriggerKind.ThesisBreak, triggerRef);
        recommendation.Rationale = $"Your thesis on {ticker} broke on {broken.BrokenAt:yyyy-MM-dd}: {reason}. "
            + "The reason you hold it no longer stands, so the rule is to exit.";
        recommendation.InvalidationTrigger = "You revise the thesis with a new invalidation rule that still holds.";
        recommendation.Evidence =
        [
            new(RecommendationEvidenceKind.Thesis, $"Thesis broken: {reason}", triggerRef),
        ];

        return await SizeAndWriteAsync(recommendation, position, cap: null, ct);
    }

    private async Task<bool> TryProposeTrimAsync(
        Guid userId, BookFigures book, string ticker, decimal usd, decimal cap, InvestmentThesis? thesis, CancellationToken ct)
    {
        var triggerRef = RecommendationEvidenceFactory.MaxPositionWeightRule;
        if (await repo.ExistsForTriggerSinceUnscopedAsync(
            userId, ticker, RecommendationTriggerKind.RiskRule, triggerRef, DateTimeOffset.UtcNow.AddDays(-CapCooldownDays), ct))
        {
            return false;
        }

        var position = await positions.ReadAsync(book, ticker, ct);
        if (position is not { Held: true })
        {
            return false;
        }

        var weight = usd / book.TotalValueUsd;
        var recommendation = NewRuleProposal(userId, ticker, RecommendationAction.Trim, RecommendationTriggerKind.RiskRule, triggerRef);
        recommendation.Rationale = string.Create(
            CultureInfo.InvariantCulture,
            $"{ticker} is {weight * Percent:0.0}% of the book, over your {cap * Percent:0.#}% single-position cap. Trimming the excess brings it back to the cap.");
        recommendation.InvalidationTrigger = "The position falls back under the cap on its own, or you raise the cap in your risk policy.";
        recommendation.Evidence =
        [
            RecommendationEvidenceFactory.OverCap(weight, cap),
            thesis is null
                ? RecommendationEvidenceFactory.NoThesis()
                : new(RecommendationEvidenceKind.Thesis, "Thesis on file", thesis.Id.ToString()),
        ];

        return await SizeAndWriteAsync(recommendation, position, cap, ct);
    }

    private async Task<bool> SizeAndWriteAsync(
        Recommendation recommendation, RecommendationPosition position, decimal? cap, CancellationToken ct)
    {
        var size = RecommendationSizing.Size(new RecommendationSizingInput(
            recommendation.Action, position.Quantity, position.PositionUsd, position.BookTotalUsd, position.UnitPriceUsd,
            position.Fractional, RequestedUsd: null, cap));
        if (size is null)
        {
            return false;
        }

        RecommendationSizer.Apply(recommendation, size, position);
        await writer.WriteAsync(recommendation, ct);
        return true;
    }

    private static Recommendation NewRuleProposal(
        Guid userId, string ticker, RecommendationAction action, RecommendationTriggerKind kind, string triggerRef)
        => new()
        {
            UserId = userId,
            Ticker = ticker,
            Action = action,
            Origin = RecommendationOrigin.Rules,
            TriggerKind = kind,
            TriggerRef = triggerRef,
            Confidence = RecommendationConfidence.High,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(ExpiryDays),
        };
}
