namespace FinanceSentry.Core.Measures;

/// <summary>
/// M1: how much more or less there is. <paramref name="ChangeUsd"/> is end minus start;
/// <paramref name="Percent"/> is that change over the start. The percent includes deposits and withdrawals,
/// so it is labelled as such wherever it is shown; it is not a return.
/// </summary>
public sealed record ValueChangeResult(decimal ChangeUsd, MeasuredRate Percent);

/// <summary>M1, value change (finance-framework §3.4). Pure: no I/O, no clock.</summary>
public static class ValueChange
{
    /// <param name="startUsd">The scope's value at the window's opening.</param>
    /// <param name="endUsd">The scope's value at the window's closing.</param>
    /// <param name="scopeChanged">Whether an account was linked or unlinked during the window. The money
    /// change stands; the percent does not, because linking an existing account is not a change in wealth
    /// (rule 3).</param>
    public static ValueChangeResult Measure(decimal startUsd, decimal endUsd, bool scopeChanged)
    {
        var change = endUsd - startUsd;
        var percent = startUsd switch
        {
            0m => MeasuredRate.Fails(MeasureFailure.ZeroBase),
            < 0m => MeasuredRate.Fails(MeasureFailure.NegativeBase),
            _ when scopeChanged => MeasuredRate.Fails(MeasureFailure.ScopeChanged),
            _ => MeasuredRate.Of(change / startUsd),
        };

        return new ValueChangeResult(change, percent);
    }
}
