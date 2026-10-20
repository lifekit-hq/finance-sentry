namespace FinanceSentry.Infrastructure.Fx;

using System.ComponentModel.DataAnnotations;

/// <summary>The <c>ExchangeRates</c> configuration section: overridable base URLs of the three rate feeds.</summary>
public sealed class ExchangeRatesOptions
{
    public const string SectionName = "ExchangeRates";

    [Url]
    public string BaseUrl { get; set; } = "https://open.er-api.com/";

    [Url]
    public string NbuBaseUrl { get; set; } = "https://bank.gov.ua/";

    [Url]
    public string FrankfurterBaseUrl { get; set; } = "https://api.frankfurter.dev/";
}
