namespace FinanceSentry.Modules.BankSync.Domain;

/// <summary>The values <see cref="BankAccount.ClosedReason"/> and <see cref="BankAccount.ClosedBy"/> take.</summary>
public static class AccountClosedReasons
{
    /// <summary>The provider's list no longer shows the account and its own health says the resource is gone.</summary>
    public const string AbsentFromList = "absent_from_list";

    /// <summary>A new account with the same IBAN replaced it (card reissue).</summary>
    public const string Reissued = "reissued";

    /// <summary><see cref="BankAccount.ClosedBy"/> of every retirement discovery performs.</summary>
    public const string BySystem = "system";
}
