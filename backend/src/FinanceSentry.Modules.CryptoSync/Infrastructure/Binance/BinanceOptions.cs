namespace FinanceSentry.Modules.CryptoSync.Infrastructure.Binance;

using System.ComponentModel.DataAnnotations;

/// <summary>The <c>Binance</c> configuration section. Every key is optional; the defaults are production values.</summary>
public sealed class BinanceOptions
{
    public const string SectionName = "Binance";

    [Url]
    public string BaseUrl { get; set; } = "https://api.binance.com";

    [Range(1, 60_000)]
    public int RecvWindowMs { get; set; } = 5000;

    [Range(0, double.MaxValue)]
    public decimal DustThresholdUsd { get; set; } = 0.01m;
}
