namespace FinanceSentry.Modules.BankSync.Infrastructure.Monobank;

using System.ComponentModel.DataAnnotations;

/// <summary>The <c>Monobank</c> configuration section. Optional; the default is the production API.</summary>
public sealed class MonobankOptions
{
    public const string SectionName = "Monobank";

    [Url]
    public string BaseUrl { get; set; } = "https://api.monobank.ua";
}
