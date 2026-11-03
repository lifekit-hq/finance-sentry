namespace FinanceSentry.Modules.BankSync.Application.Services.DetectionPhrases;

using static FinanceSentry.Modules.BankSync.Domain.DetectionPhraseKinds;

/// <summary>
/// The shipped contents of <c>detection_phrases</c>: the wording the detection code used to hold in
/// constants, row for row, in the order it was tried. Generic statement vocabulary only; nothing here
/// comes from one person's book. <c>CategorySeeder</c> inserts the rows a database lacks.
/// </summary>
public static class DetectionPhraseSeedData
{
    private const string Monobank = "monobank";
    private const string TrueLayer = "truelayer";
    private const string Aib = "aib";
    private const string English = "en";
    private const string Ukrainian = "uk";

    /// <summary>MCC Monobank books installment repayments and card-to-card transfers under (wire transfer).</summary>
    private const int WireTransferMcc = 4829;

    public static IReadOnlyList<DetectionPhraseEntry> Entries { get; } =
    [
        // Normalized merchant keys that name no merchant (SubscriptionDetectionAlgorithm).
        new(NotAMerchant, "unknown", Locale: English),
        new(NotAMerchant, "transfer", Locale: English),
        new(NotAMerchant, "top-up", Locale: English),
        new(NotAMerchant, "topup", Locale: English),
        new(NotAMerchant, "recharge", Locale: English),
        new(NotAMerchant, "withdrawal", Locale: English),
        new(NotAMerchant, "atm", Locale: English),
        new(NotAMerchant, "cash", Locale: English),
        new(NotAMerchant, "переказ", Locale: Ukrainian),

        // Installment (розстрочка) repayments (InstallmentPlanRecognizer). Verified against live
        // Monobank data: «Погашення наступного платежу RozetkaPay», «Щомісячний платіж telemart -
        // monomarket», «Платіж Pandora», «Повне погашення RozetkaPay».
        new(InstallmentMarker, "погашення наступного платежу", Monobank, Ukrainian),
        new(InstallmentMarker, "повне погашення", Monobank, Ukrainian),
        new(InstallmentMarker, "щомісячний платіж", Monobank, Ukrainian),
        new(InstallmentMarker, "monomarket", Monobank),
        new(InstallmentMarker, "розстроч", Monobank, Ukrainian),
        new(InstallmentMarker, "оплата частинами", Monobank, Ukrainian),
        new(InstallmentMarker, "покупка частинами", Monobank, Ukrainian),
        new(InstallmentMarker, "частинами", Monobank, Ukrainian),
        new(InstallmentMarker, "installment", Locale: English),
        new(InstallmentPayoffMarker, "повне погашення", Monobank, Ukrainian),
        new(InstallmentMerchantPrefix, "погашення наступного платежу", Monobank, Ukrainian),
        new(InstallmentMerchantPrefix, "повне погашення", Monobank, Ukrainian),
        new(InstallmentMerchantPrefix, "щомісячний платіж", Monobank, Ukrainian),
        new(InstallmentMerchantPrefix, "платіж", Monobank, Ukrainian),
        new(InstallmentMerchantTag, "monomarket", Monobank),
        new(InstallmentTransferPrefix, "Платіж ", Monobank, Ukrainian, MccCondition: WireTransferMcc),

        // Directional transfer wording (TransferDescriptionClassifier). TrueLayer feeds render
        // "To …" / "From …" / "Payment from …"; Monobank renders savings-jar moves as
        // «Поповнення «Jar»» and «З банки «Jar»».
        new(IncomePrefix, "Payment from ", TrueLayer, English),
        new(TransferInPrefix, "From ", TrueLayer, English),
        new(TransferInPrefix, "З банки ", Monobank, Ukrainian),
        new(TransferOutPrefix, "To ", TrueLayer, English),
        new(TransferOutPrefix, "Поповнення ", Monobank, Ukrainian),

        // AIB renders a cash lodgement made at an ATM as "*ATMLDG <branch>" (CashLodgementClassifier).
        new(CashLodgementMarker, "*ATMLDG", Aib, English),
    ];
}
