// Ported from ExcelFinancialFunctions (https://github.com/fsprojects/ExcelFinancialFunctions, commit 97756a3,
// src/ExcelFinancialFunctions/irr.fs and common.fs), Copyright 2010-2013, licensed under the Apache License,
// Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0). See backend/THIRD-PARTY-NOTICES.txt.
//
// Changes from the original: translated from F# to C#; every failure the original throws (no positive and
// negative cash flow, no bracketing interval, no root within the iteration limits) is returned as a typed
// MeasureFailure instead; the mutable module-level precision is replaced by constants; a date before the first
// date is rejected as InvalidInput; a Newton result that is not finite falls through to bisection.
namespace FinanceSentry.Core.Measures;

/// <summary>One dated cash flow for <see cref="Xirr"/>: negative is money paid in, positive is money taken out.</summary>
public readonly record struct DatedCashFlow(DateOnly Date, double Amount);

/// <summary>An annual rate from <see cref="Xirr"/>, or the typed reason there is none.</summary>
public readonly record struct XirrOutcome(double? Rate, MeasureFailure? Failure);

/// <summary>
/// XIRR: the annual rate r at which Σ cf_i / (1 + r)^(days_i / 365) = 0, days counted from the first cash
/// flow's date (actual/365, as Excel, LibreOffice and ExcelFinancialFunctions compute it). Newton first; if it
/// does not converge, or lands on the other side of zero from the guess, an interval bracketing the root is
/// searched for and bisected.
/// </summary>
public static class Xirr
{
    private const double DefaultGuess = 0.1;
    private const double DaysPerYear = 365d;
    private const double Precision = 0.000001;
    private const int NewtonMaxCount = 20;
    private const double BoundsShift = 0.01;
    private const double BoundsFactor = 1.6;
    private const int BoundsMaxTries = 60;
    private const double MinRate = -1d;
    private const int BisectionMaxCount = 200;

    /// <param name="cashFlows">The cash flows in date order of the first one: no date may precede the first.</param>
    /// <param name="guess">The starting rate for Newton's method.</param>
    public static XirrOutcome Solve(IReadOnlyList<DatedCashFlow> cashFlows, double guess = DefaultGuess)
    {
        if (cashFlows.Count == 0 || cashFlows.Any(c => c.Date < cashFlows[0].Date || !double.IsFinite(c.Amount)))
            return Fail(MeasureFailure.InvalidInput);

        // As the original: a zero cash flow counts on the paying-in side.
        if (!cashFlows.Any(c => c.Amount > 0d) || !cashFlows.Any(c => c.Amount <= 0d))
            return Fail(MeasureFailure.NoSignChange);

        var first = cashFlows[0].Date;
        var years = cashFlows.Select(c => (c.Date.DayNumber - first.DayNumber) / DaysPerYear).ToArray();
        double Npv(double rate)
        {
            var sum = 0d;
            for (var i = 0; i < cashFlows.Count; i++)
                sum += cashFlows[i].Amount / Math.Pow(1d + rate, years[i]);
            return sum;
        }

        return FindRoot(Npv, guess);
    }

    private static XirrOutcome FindRoot(Func<double, double> f, double guess)
    {
        var newton = Newton(f, guess);
        if (newton is double root && double.IsFinite(root) && Math.Sign(guess) == Math.Sign(root))
            return new XirrOutcome(root, null);

        var bounds = FindBounds(f, guess, MinRate, double.MaxValue);
        return bounds is (double lower, double upper)
            ? Bisection(f, lower, upper)
            : Fail(MeasureFailure.NoConvergence);
    }

    private static double? Newton(Func<double, double> f, double x)
    {
        for (var count = 0; ; count++)
        {
            var derivative = (f(x + Precision) - f(x - Precision)) / (2d * Precision);
            var next = x - (f(x) / derivative);
            if (Math.Abs(next - x) < Precision)
                return next;
            if (count > NewtonMaxCount)
                return null;
            x = next;
        }
    }

    private static (double Lower, double Upper)? FindBounds(
        Func<double, double> f, double guess, double minBound, double maxBound)
    {
        if (guess <= minBound || guess >= maxBound)
            return null;

        double AdjustToMin(double value) => value <= minBound ? minBound + Precision : value;
        double AdjustToMax(double value) => value >= maxBound ? maxBound - Precision : value;

        var low = AdjustToMin(guess - BoundsShift);
        var up = AdjustToMax(guess + BoundsShift);

        // The original counts down from maxTries and gives up when the count reaches zero.
        for (var tries = BoundsMaxTries - 1; tries > 0; tries--)
        {
            var lower = AdjustToMin(low);
            var upper = AdjustToMax(up);
            var product = f(lower) * f(upper);
            if (product <= 0d)
                return (lower, upper);
            if (!(product > 0d))
                return null;

            low = lower + (BoundsFactor * (lower - upper));
            up = upper + (BoundsFactor * (upper - lower));
        }

        return null;
    }

    private static XirrOutcome Bisection(Func<double, double> f, double a, double b)
    {
        for (var count = 0; ; count++)
        {
            if (a == b)
                return Fail(MeasureFailure.NoConvergence);

            var fa = f(a);
            if (Math.Abs(fa) < Precision)
                return new XirrOutcome(a, null);
            var fb = f(b);
            if (Math.Abs(fb) < Precision)
                return new XirrOutcome(b, null);
            if (count + 1 > BisectionMaxCount || fa * fb > 0d)
                return Fail(MeasureFailure.NoConvergence);

            var mid = a + (0.5 * (b - a));
            var fmid = f(mid);
            if (Math.Abs(fmid) < Precision)
                return new XirrOutcome(mid, null);
            if (fa * fmid < 0d)
                b = mid;
            else if (fa * fmid > 0d)
                a = mid;
            else
                return Fail(MeasureFailure.NoConvergence);
        }
    }

    private static XirrOutcome Fail(MeasureFailure reason) => new(null, reason);
}
