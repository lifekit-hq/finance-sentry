namespace FinanceSentry.Modules.Research.Application.Commands;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain.Exceptions;
using FinanceSentry.Modules.Research.Domain.Ports;
using FinanceSentry.Modules.Research.Domain.Recommendations;
using FinanceSentry.Modules.Research.Domain.Repositories;

/// <summary>
/// Ledger proposes one action on one ticker (#1069). The proposal must be anchored to a trigger that is true right now
/// - a broken thesis, an IPS sleeve outside its band, a position over the single-position cap, a recent Radar signal,
/// or a fired event - and Hold is only an answer to a fired event. The handler sizes it from the book and a quote
/// (Add runs the risk gate and is clamped to the compliant size), writes the exact order text the owner would place at
/// the broker, and queues it silently. Nothing here or downstream sends an order anywhere.
/// </summary>
public sealed record ProposeRecommendationCommand(
    Guid UserId,
    string Ticker,
    RecommendationAction Action,
    RecommendationTriggerKind TriggerKind,
    string TriggerRef,
    string Rationale,
    RecommendationConfidence Confidence,
    string InvalidationTrigger,
    IReadOnlyList<RecommendationEvidence> Evidence,
    decimal? SizeUsd = null,
    int? ExpiresInDays = null) : ICommand<RecommendationDto>;

public sealed class ProposeRecommendationCommandHandler(
    IThesisRepository theses,
    IPositionCapSource capSource,
    IAllocationDriftReader drift,
    IAssetSignalReader signals,
    IFiredEventSource firedEvents,
    IRiskPolicyGate riskGate,
    RecommendationPositionReader positions,
    RecommendationWriter writer)
    : ICommandHandler<ProposeRecommendationCommand, RecommendationDto>
{
    public const int DefaultExpiryDays = 14;
    public const int MaxExpiryDays = 30;
    public const int RadarSignalWindowDays = 30;
    private const int MaxTickerLength = 20;
    private const int RadarSignalLookup = 20;
    private const string OverBand = "OverBand";
    private const string UnderBand = "UnderBand";

    public async Task<RecommendationDto> Handle(ProposeRecommendationCommand command, CancellationToken ct)
    {
        var ticker = (command.Ticker ?? string.Empty).Trim().ToUpperInvariant();
        Validate(command, ticker);

        var evidence = command.Evidence
            .Select(e => new RecommendationEvidence(e.Kind, e.Label.Trim(), string.IsNullOrWhiteSpace(e.RefId) ? null : e.RefId.Trim()))
            .ToList();

        var position = await positions.ReadAsync(command.UserId, ticker, ct);
        if (command.Action is RecommendationAction.Trim or RecommendationAction.Exit && position is not { Held: true })
        {
            throw new InvalidRecommendationException($"{ticker} is not held, so there is nothing to {command.Action.ToString().ToLowerInvariant()}.");
        }

        var cap = await capSource.GetMaxPositionWeightAsync(command.UserId, ct);
        var triggerRef = await CheckTriggerAsync(command, ticker, position, cap, evidence, ct);

        var requestedUsd = command.SizeUsd;
        if (command.Action == RecommendationAction.Add)
        {
            requestedUsd = await GateAddAsync(command.UserId, ticker, command.SizeUsd!.Value, evidence, ct);
        }

        var recommendation = new Recommendation
        {
            UserId = command.UserId,
            Ticker = ticker,
            Action = command.Action,
            Origin = RecommendationOrigin.Ledger,
            TriggerKind = command.TriggerKind,
            TriggerRef = triggerRef,
            Rationale = command.Rationale.Trim(),
            Confidence = command.Confidence,
            InvalidationTrigger = command.InvalidationTrigger.Trim(),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(command.ExpiresInDays ?? DefaultExpiryDays),
        };

        if (command.Action != RecommendationAction.Hold)
        {
            if (position is null)
            {
                throw new InvalidRecommendationException($"No price for {ticker}, so the proposal cannot be sized.");
            }

            var size = RecommendationSizing.Size(new RecommendationSizingInput(
                command.Action, position.Quantity, position.PositionUsd, position.BookTotalUsd, position.UnitPriceUsd,
                position.Fractional, requestedUsd, cap))
                ?? throw new InvalidRecommendationException(command.Action == RecommendationAction.Trim
                    ? $"{ticker} is within the single-position cap; give sizeUsd for a discretionary trim."
                    : $"The proposal on {ticker} sizes to nothing.");
            RecommendationSizer.Apply(recommendation, size, position);
        }

        recommendation.Evidence = evidence.Take(RecommendationEvidence.MaxCount).ToList();
        await writer.WriteAsync(recommendation, ct);
        return RecommendationDto.From(recommendation);
    }

    private static void Validate(ProposeRecommendationCommand c, string ticker)
    {
        static void Require(bool ok, string reason)
        {
            if (!ok)
            {
                throw new InvalidRecommendationException(reason);
            }
        }

        Require(ticker.Length is > 0 and <= MaxTickerLength, "ticker is required (at most 20 characters).");
        Require(Enum.IsDefined(c.Action), "action must be Add, Hold, Trim or Exit.");
        Require(Enum.IsDefined(c.TriggerKind), "triggerKind must be ThesisBreak, IpsBand, RiskRule, RadarSignal or EventVerdict.");
        Require(Enum.IsDefined(c.Confidence), "confidence must be Low, Medium or High.");
        Require(!string.IsNullOrWhiteSpace(c.Rationale) && c.Rationale.Trim().Length <= Recommendation.MaxRationaleLength,
            "rationale is required (at most 2000 characters) and must cite the numbers behind the call.");
        Require(!string.IsNullOrWhiteSpace(c.InvalidationTrigger)
            && c.InvalidationTrigger.Trim().Length <= Recommendation.MaxInvalidationLength,
            "invalidationTrigger is required (at most 500 characters): what would make this call wrong.");
        Require((c.TriggerRef ?? string.Empty).Trim().Length <= Recommendation.MaxTriggerRefLength,
            "triggerRef is at most 120 characters.");
        Require(c.Action != RecommendationAction.Hold || c.TriggerKind == RecommendationTriggerKind.EventVerdict,
            "Hold is only an answer to a fired event: use triggerKind EventVerdict with the event id.");
        Require(c.Action != RecommendationAction.Add || c.SizeUsd is > 0m, "Add needs sizeUsd greater than zero.");
        Require(c.SizeUsd is null or > 0m, "sizeUsd must be greater than zero when given.");
        Require(c.ExpiresInDays is null or (>= 1 and <= MaxExpiryDays), "expiresInDays must be 1 to 30.");
        Require(c.Evidence.Count <= RecommendationEvidence.MaxCount, "At most 12 evidence items.");
        foreach (var e in c.Evidence)
        {
            Require(RecommendationEvidenceKind.All.Contains(e.Kind),
                $"evidence kind must be one of {string.Join(", ", RecommendationEvidenceKind.All)}.");
            Require(!string.IsNullOrWhiteSpace(e.Label) && e.Label.Trim().Length <= RecommendationEvidence.MaxLabelLength,
                "Each evidence item needs a label (at most 300 characters).");
            Require((e.RefId ?? string.Empty).Length <= RecommendationEvidence.MaxRefIdLength,
                "evidence refId is at most 120 characters.");
        }
    }

    // The trigger must hold now; it is added to the evidence so the card shows what fired.
    private async Task<string> CheckTriggerAsync(
        ProposeRecommendationCommand c, string ticker, RecommendationPosition? position, decimal? cap,
        List<RecommendationEvidence> evidence, CancellationToken ct)
    {
        var triggerRef = (c.TriggerRef ?? string.Empty).Trim();
        switch (c.TriggerKind)
        {
            case RecommendationTriggerKind.ThesisBreak:
            {
                var broken = (await theses.FindByTickerAsync(c.UserId, ticker, ct))
                    .Where(t => t.BrokenAt is not null)
                    .OrderByDescending(t => t.BrokenAt)
                    .FirstOrDefault()
                    ?? throw new InvalidRecommendationException($"No broken thesis on {ticker}.");
                evidence.Insert(0, new RecommendationEvidence(
                    RecommendationEvidenceKind.Thesis, $"Thesis broken: {broken.BrokenReason ?? "an invalidation trigger fired"}", broken.Id.ToString()));
                return broken.Id.ToString();
            }

            case RecommendationTriggerKind.IpsBand:
            {
                var reading = await drift.GetAsync(c.UserId, ct);
                var sleeve = reading.Sleeves.FirstOrDefault(s => s.AssetClass.Equals(triggerRef, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidRecommendationException("triggerRef must name an IPS sleeve (asset class) for an IpsBand trigger.");
                var expected = c.Action == RecommendationAction.Add ? UnderBand : OverBand;
                if (!sleeve.Status.Equals(expected, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidRecommendationException(
                        $"The {sleeve.AssetClass} sleeve is {sleeve.Status}, not {expected}, so it does not call for {c.Action}.");
                }

                evidence.Insert(0, new RecommendationEvidence(
                    RecommendationEvidenceKind.Sleeve,
                    $"{sleeve.AssetClass} sleeve {sleeve.ActualPct:0.#}% vs target {sleeve.TargetPct:0.#}% ({sleeve.Status})",
                    sleeve.AssetClass));
                return sleeve.AssetClass;
            }

            case RecommendationTriggerKind.RiskRule:
            {
                if (cap is not decimal capFraction || position is not { Held: true, BookTotalUsd: > 0m })
                {
                    throw new InvalidRecommendationException("A RiskRule trigger needs a single-position cap and a held position.");
                }

                var weight = position.PositionUsd / position.BookTotalUsd;
                if (weight <= capFraction || c.Action == RecommendationAction.Add)
                {
                    throw new InvalidRecommendationException($"{ticker} is not over the single-position cap.");
                }

                evidence.Insert(0, RecommendationEvidenceFactory.OverCap(weight, capFraction));
                return RecommendationEvidenceFactory.MaxPositionWeightRule;
            }

            case RecommendationTriggerKind.RadarSignal:
            {
                var since = DateTimeOffset.UtcNow.AddDays(-RadarSignalWindowDays);
                var recent = (await signals.GetRecentAsync(ticker, RadarSignalLookup, ct))
                    .Where(s => s.Timestamp >= since)
                    .FirstOrDefault(s => triggerRef.Length == 0
                        || s.Scanner.Equals(triggerRef, StringComparison.OrdinalIgnoreCase)
                        || s.SignalType.Equals(triggerRef, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidRecommendationException($"No Radar signal on {ticker} in the last {RadarSignalWindowDays} days.");
                evidence.Insert(0, new RecommendationEvidence(
                    RecommendationEvidenceKind.RadarSignal,
                    $"Radar {recent.Scanner}: {recent.SignalType} ({recent.Severity}) on {recent.Timestamp:yyyy-MM-dd}",
                    recent.Scanner));
                return recent.Scanner;
            }

            case RecommendationTriggerKind.EventVerdict:
            {
                if (!Guid.TryParse(triggerRef, out var eventId))
                {
                    throw new InvalidRecommendationException("triggerRef must be the fired event's id for an EventVerdict trigger.");
                }

                var fired = await firedEvents.FindAsync(c.UserId, eventId, ct)
                    ?? throw new InvalidRecommendationException("No fired event with that id.");
                evidence.Insert(0, new RecommendationEvidence(
                    RecommendationEvidenceKind.Event, $"{fired.Kind}: {fired.Subject} ({fired.OccurredAt:yyyy-MM-dd})", fired.EventId.ToString()));
                return fired.EventId.ToString();
            }

            default:
                throw new InvalidRecommendationException("Unknown trigger kind.");
        }
    }

    // An Add runs the same pre-trade gate a promotion does; a refusal clamps to the compliant size or rejects.
    private async Task<decimal> GateAddAsync(
        Guid userId, string ticker, decimal sizeUsd, List<RecommendationEvidence> evidence, CancellationToken ct)
    {
        var verdict = await riskGate.CheckProposalAsync(userId, ticker, sizeUsd, overrideFlag: false, isPaper: false, ct);
        if (verdict.Decision == RiskGateDecision.Allowed)
        {
            return sizeUsd;
        }

        if (verdict.MaxCompliantSizeUsd is decimal max && max > 0m)
        {
            evidence.Add(new RecommendationEvidence(
                RecommendationEvidenceKind.RiskRule,
                $"Sized down from ${sizeUsd:0} to ${max:0} to stay within {verdict.RuleKey ?? "the risk policy"}",
                verdict.RuleKey));
            return max;
        }

        throw new InvalidRecommendationException(
            $"The risk policy refuses adding to {ticker}: {verdict.Note ?? verdict.RuleKey ?? "no compliant size"}.");
    }
}
