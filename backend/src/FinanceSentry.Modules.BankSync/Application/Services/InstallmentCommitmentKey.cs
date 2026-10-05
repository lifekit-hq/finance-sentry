namespace FinanceSentry.Modules.BankSync.Application.Services;

/// <summary>
/// The key an installment added from a picked charge is stored under: the plan-key shape
/// <c>installment:{merchant}:{roundedAmount}</c>, so only charges of the same amount at the same
/// merchant count toward it and advance it. A charge the plan recognizer already keys that way
/// keeps its key.
/// </summary>
public static class InstallmentCommitmentKey
{
    private const string Prefix = "installment:";

    public static string Resolve(string? merchantName, string? description, decimal amount, int? mcc)
    {
        var key = CommitmentKeyResolver.Resolve(merchantName, description, amount, mcc);
        return key.StartsWith(Prefix, StringComparison.Ordinal) ? key : InstallmentPlanRecognizer.PlanKey(key, amount);
    }
}
