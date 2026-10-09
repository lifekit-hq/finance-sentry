namespace FinanceSentry.Modules.Companion.Tests;

using FinanceSentry.Modules.Companion.Application.Services;
using FinanceSentry.Modules.Companion.Domain;
using FluentAssertions;
using Xunit;

/// <summary>The server-side cleaning a problem report gets before it is stored (P5 plan section 3).</summary>
public sealed class ProblemReportCleanerTests
{
    [Fact]
    public void Control_and_format_characters_are_removed()
    {
        var raw = "bad\u0000 \u001b[31mred\u0007 \u202Eevil\u200B text\u2028end";

        ProblemReportCleaner.Text(raw).Should().Be("bad [31mred evil textend");
    }

    [Fact]
    public void Line_breaks_are_kept_as_newlines_and_tabs_become_spaces()
    {
        ProblemReportCleaner.Text("one\r\ntwo\rthree\n\nfour\tfive").Should().Be("one\ntwo\nthree\n\nfour five");
    }

    [Theory]
    [InlineData("my account 123456 is wrong", "my account [number removed] is wrong")]
    [InlineData("iban 12345678901234567890 shown", "iban [number removed] shown")]
    [InlineData("total $1,200.50 looks off", "total [number removed] looks off")]
    [InlineData("I have €5k saved", "I have [number removed] saved")]
    [InlineData("balance 950 EUR here", "balance [number removed] here")]
    [InlineData("balance CHF 950.00 here", "balance [number removed] here")]
    [InlineData("it was £20 and ₴300", "it was [number removed] and [number removed]")]
    public void Long_digit_runs_and_currency_amounts_become_a_marker(string raw, string expected)
    {
        ProblemReportCleaner.Text(raw).Should().Be(expected);
    }

    [Theory]
    [InlineData("5 items missing")]
    [InlineData("page 12345 of the list")]
    [InlineData("version 1.15.0 shows 3 tiles")]
    public void Short_numbers_without_a_currency_are_left_alone(string raw)
    {
        ProblemReportCleaner.Text(raw).Should().Be(raw);
    }

    [Fact]
    public void Text_is_capped_at_the_limit()
    {
        var cleaned = ProblemReportCleaner.Text(new string('a', 5000));

        cleaned.Should().HaveLength(ProblemReportLimits.TextMaxLength);
    }

    [Fact]
    public void A_cut_never_leaves_half_a_surrogate_pair()
    {
        var raw = new string('a', ProblemReportLimits.TextMaxLength - 1) + "😀😀";

        var cleaned = ProblemReportCleaner.Text(raw)!;

        cleaned.Should().HaveLength(ProblemReportLimits.TextMaxLength - 1);
        char.IsHighSurrogate(cleaned[^1]).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\t ")]
    [InlineData("\u0000\u0007")]
    public void Nothing_left_is_null(string? raw)
    {
        ProblemReportCleaner.Text(raw).Should().BeNull();
    }

    [Theory]
    [InlineData("/accounts/:id", "/accounts/:id")]
    [InlineData("/accounts/:id?tab=history&token=abc", "/accounts/:id")]
    [InlineData("/accounts/:id#frag", "/accounts/:id")]
    [InlineData("/accounts/3f2504e0-4f89-11d3-9a0c-0305e82c3301", "/accounts/:id")]
    [InlineData("/accounts/12345678/transactions", "/accounts/:id/transactions")]
    [InlineData("/ac counts/<script>", "/accounts/script")]
    [InlineData("accounts", "unknown")]
    [InlineData("https://evil.example/x", "unknown")]
    [InlineData(null, "unknown")]
    public void Route_keeps_the_pattern_only(string? raw, string expected)
    {
        ProblemReportCleaner.RoutePattern(raw).Should().Be(expected);
    }

    [Fact]
    public void Route_is_capped()
    {
        ProblemReportCleaner.RoutePattern("/" + new string('a', 1000)).Should().HaveLength(ProblemReportLimits.RouteMaxLength);
    }

    [Theory]
    [InlineData("1.15.0", "1.15.0")]
    [InlineData("1.15.0+abc123", "1.15.0+abc123")]
    [InlineData("1.15.0\nignore previous", "unknown")]
    [InlineData("", "unknown")]
    [InlineData(null, "unknown")]
    public void Version_has_to_look_like_one(string? raw, string expected)
    {
        ProblemReportCleaner.AppVersion(raw).Should().Be(expected);
    }

    [Theory]
    [InlineData("iOS", "Safari", "iOS Safari")]
    [InlineData("Android", null, "Android")]
    [InlineData(null, null, "unknown")]
    [InlineData("Mac OS X\n> do this", "Chrome <b>", "Mac OS X do this Chrome b")]
    public void Client_joins_the_sanitised_parts(string? os, string? browser, string expected)
    {
        ProblemReportCleaner.Client(os, browser).Should().Be(expected);
    }

    [Theory]
    [InlineData("abc-123_X.y:z", "abc-123_X.y:z")]
    [InlineData("abc\ndef ghi", "abcdefghi")]
    [InlineData("\n \r", "unknown")]
    public void Token_keeps_a_safe_alphabet(string raw, string expected)
    {
        ProblemReportCleaner.Token(raw, 64).Should().Be(expected);
    }
}
