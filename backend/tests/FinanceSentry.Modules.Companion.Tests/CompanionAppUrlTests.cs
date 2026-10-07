namespace FinanceSentry.Modules.Companion.Tests;

using FinanceSentry.Modules.Companion.Application.Services;
using FluentAssertions;
using Xunit;

public sealed class CompanionAppUrlTests
{
    [Theory]
    [InlineData("https://app.example.com", "/assets/NVDA", "https://app.example.com/assets/NVDA")]
    [InlineData("https://app.example.com/", "/assets/NVDA", "https://app.example.com/assets/NVDA")]
    [InlineData(" http://localhost:4200 ", "/alerts", "http://localhost:4200/alerts")]
    [InlineData("https://host.example.com/finance/", "/transactions?type=debit&from=2026-10-01", "https://host.example.com/finance/transactions?type=debit&from=2026-10-01")]
    public void Joins_the_path_onto_the_base_url(string baseUrl, string path, string expected)
        => CompanionAppUrl.For(baseUrl, path).Should().Be(expected);

    [Theory]
    [InlineData(null, "/assets/NVDA")]
    [InlineData("", "/assets/NVDA")]
    [InlineData("   ", "/assets/NVDA")]
    [InlineData("app.example.com", "/assets/NVDA")]
    [InlineData("/relative", "/assets/NVDA")]
    [InlineData("ftp://app.example.com", "/assets/NVDA")]
    [InlineData("https://app.example.com", null)]
    [InlineData("https://app.example.com", "")]
    [InlineData("https://app.example.com", "assets/NVDA")]
    [InlineData("https://app.example.com", "//evil.example.com/x")]
    [InlineData("https://app.example.com", "https://evil.example.com/x")]
    public void Yields_nothing_rather_than_a_relative_or_off_site_link(string? baseUrl, string? path)
        => CompanionAppUrl.For(baseUrl, path).Should().BeNull();
}
