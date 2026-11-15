namespace FinanceSentry.Modules.Research.Application.Commands;

using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Revisions;

/// <summary>The thesis as it would stand with a revision's non-null fields laid over it.</summary>
public static class ThesisRevisionMerge
{
    public static ThesisVersion Apply(ThesisVersion current, ThesisRevisionContent content) => current with
    {
        ThesisText = content.ThesisText ?? current.ThesisText,
        EntryPrice = content.EntryPrice ?? current.EntryPrice,
        KeyDataPoints = content.KeyDataPoints ?? current.KeyDataPoints,
        Catalysts = content.Catalysts ?? current.Catalysts,
        InvalidationTriggers = content.InvalidationTriggers ?? current.InvalidationTriggers,
        NarrativeFalsifiers = content.NarrativeFalsifiers ?? current.NarrativeFalsifiers,
    };
}
