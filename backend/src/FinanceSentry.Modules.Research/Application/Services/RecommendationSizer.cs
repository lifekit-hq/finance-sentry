namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Modules.Research.Domain.Recommendations;

/// <summary>Copies a computed size and the matching order text onto a proposal.</summary>
public static class RecommendationSizer
{
    public static void Apply(Recommendation recommendation, RecommendationSize size, RecommendationPosition position)
    {
        recommendation.SizeUsd = size.SizeUsd;
        recommendation.SizeShares = size.SizeShares;
        recommendation.ShareOfBookBeforePct = size.ShareOfBookBeforePct;
        recommendation.ShareOfBookAfterPct = size.ShareOfBookAfterPct;
        recommendation.LimitPrice = position.LimitPrice;
        recommendation.LimitCurrency = position.LimitCurrency;
        recommendation.Venue = position.Venue;
        recommendation.OrderText = RecommendationOrderText.Build(
            recommendation.Action, recommendation.Ticker, size.SizeShares, position.LimitPrice, position.LimitCurrency, position.Venue);
    }
}
