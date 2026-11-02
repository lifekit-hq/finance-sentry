namespace FinanceSentry.Modules.BankSync.Application.Services;

/// <summary>
/// The payday-transfer arithmetic: the UAH to cover (card amount owed plus fixed monthly outflow) converted to EUR at the
/// bank's buy rate (the bank buys the EUR), the exact figure rounded to cents, and the suggested transfer as that figure
/// rounded UP to a multiple of the rounding step. The cushion is the difference; the margin is the step, never an add-on.
/// </summary>
public static class PaydayTransferCalculator
{
    private const int CentsScale = 2;

    public static PaydayTransferAmounts Calculate(decimal owedUah, decimal fixedOutflowUah, decimal rateBuy, decimal roundingStepEur)
    {
        if (rateBuy <= 0m) throw new ArgumentOutOfRangeException(nameof(rateBuy), "The buy rate must be positive.");
        if (roundingStepEur <= 0m) throw new ArgumentOutOfRangeException(nameof(roundingStepEur), "The rounding step must be positive.");

        var totalUah = owedUah + fixedOutflowUah;
        // Cents first: a quotient like 900.0000001 must not push the suggestion a whole step higher.
        var exactEur = Math.Round(totalUah / rateBuy, CentsScale, MidpointRounding.AwayFromZero);
        var suggestedEur = Math.Ceiling(exactEur / roundingStepEur) * roundingStepEur;
        return new PaydayTransferAmounts(totalUah, exactEur, suggestedEur, suggestedEur - exactEur);
    }
}

public record PaydayTransferAmounts(decimal TotalUah, decimal ExactEur, decimal SuggestedEur, decimal CushionEur);
