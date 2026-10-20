namespace FinanceSentry.Modules.CryptoSync.Infrastructure.RevolutX;

using System.ComponentModel.DataAnnotations;

/// <summary>The <c>RevolutX</c> configuration section. Every key is optional; the defaults are production values.</summary>
public sealed class RevolutXOptions
{
    public const string SectionName = "RevolutX";

    [Url]
    public string BaseUrl { get; set; } = "https://revx.revolut.com";

    [Range(0, double.MaxValue)]
    public decimal DustThresholdUsd { get; set; } = 0.01m;
}
