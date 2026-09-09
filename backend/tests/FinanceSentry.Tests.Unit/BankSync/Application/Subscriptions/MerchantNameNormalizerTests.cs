namespace FinanceSentry.Tests.Unit.BankSync.Application.Subscriptions;

using FinanceSentry.Modules.BankSync.Application.Services;
using FluentAssertions;
using Xunit;

public class MerchantNameNormalizerTests
{
    [Theory]
    [InlineData("NETFLIX.COM", "netflix")]
    [InlineData("netflix.com", "netflix")]
    [InlineData("PAYPAL*SPOTIFY", "spotify")]
    [InlineData("paypal*Netflix", "netflix")]
    [InlineData("  Amzn Mktp*123  ", "amzn mktp")]
    [InlineData("SPOTIFY.NET", "spotify")]
    [InlineData("*ADOBE CC", "adobe cc")]
    [InlineData("#AMAZON", "amazon")]
    [InlineData("Apple iCloud 99", "apple icloud")]
    [InlineData("Con Edison - 4567", "con edison")]
    public void Normalize_KnownInputs_ReturnsExpected(string input, string expected)
    {
        MerchantNameNormalizer.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, "unknown")]
    [InlineData("", "unknown")]
    [InlineData("   ", "unknown")]
    [InlineData("*#  ", "unknown")]
    public void Normalize_EmptyOrWhitespace_ReturnsUnknown(string? input, string expected)
    {
        MerchantNameNormalizer.Normalize(input).Should().Be(expected);
    }

    [Fact]
    public void Normalize_CollapseInternalSpaces()
    {
        MerchantNameNormalizer.Normalize("NY   TIMES").Should().Be("ny times");
    }

    [Fact]
    public void Normalize_SameNormalizedKey_ForVariants()
    {
        var a = MerchantNameNormalizer.Normalize("Netflix.com");
        var b = MerchantNameNormalizer.Normalize("NETFLIX.COM");
        a.Should().Be(b);
    }

    [Fact]
    public void GetDisplayName_ReturnsMostFrequentNonNull()
    {
        var names = new[] { "Netflix", "NETFLIX", "Netflix", null, "NETFLIX" };
        MerchantNameNormalizer.GetDisplayName(names).Should().Be("Netflix");
    }

    [Fact]
    public void GetDisplayName_AllNull_ReturnsUnknown()
    {
        var names = new string?[] { null, null };
        MerchantNameNormalizer.GetDisplayName(names).Should().Be("unknown");
    }

    [Fact]
    public void NormalizeDetectionKey_PrefersMerchantNameOverDescription()
    {
        MerchantNameNormalizer.NormalizeDetectionKey("Netflix.com", "CARD PAYMENT 4471")
            .Should().Be("netflix");
    }

    [Fact]
    public void NormalizeDetectionKey_FallsBackToDescription_WhenMerchantNameMissing()
    {
        MerchantNameNormalizer.NormalizeDetectionKey(null, "NY   TIMES")
            .Should().Be("ny times");
    }

    [Fact]
    public void NormalizeDetectionKey_MobileTopUp_CollapsesToPerNumberKey()
    {
        // The phone number would otherwise fragment the key and trip the top-up blocklist.
        MerchantNameNormalizer.NormalizeDetectionKey(null, "*MOBI TOP-UP 0857860057")
            .Should().Be("mobile top-up 0057");
    }

    [Fact]
    public void NormalizeDetectionKey_BothMissing_ReturnsUnknown()
    {
        MerchantNameNormalizer.NormalizeDetectionKey(null, null).Should().Be("unknown");
    }

    // ── Idempotence: f(f(x)) == f(x) ─────────────────────────────────────────

    /// <summary>
    /// The statement shapes the book actually carries, plus the two that used to break
    /// idempotence: the mobile top-up key and a domain suffix hidden behind trailing digits.
    /// </summary>
    public static TheoryData<string?> MerchantShapes()
    {
        var data = new TheoryData<string?>();
        foreach (var shape in new string?[]
                 {
                     "NETFLIX.COM", "paypal*Netflix", "PAYPAL*ANTHROPIC", "  Amzn Mktp*123  ",
                     "*ADOBE CC", "#AMAZON", "Apple iCloud 99", "Con Edison - 4567", "NY   TIMES",
                     "netflix.com 12", "x.com.com", "*MOBI TOP-UP 0857860057", "mobile top-up 0057",
                     "To Mario Scalas", "Щомісячний платіж telemart - monomarket", "СІЛЬПО",
                     "unknown", "*#  ", "", "   ", null,
                 })
        {
            data.Add(shape);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(MerchantShapes))]
    public void NormalizeDetectionKey_IsAFixedPointOverItsOwnOutput(string? raw)
    {
        // Derived keys are persisted and handed back out (a pin listing, a detected
        // subscription), so callers re-derive them — unpinning by the key a listing advertised
        // is exactly that. If re-deriving moved the key, every such caller would need a second
        // lookup path to compensate.
        var key = MerchantNameNormalizer.NormalizeDetectionKey(raw, description: null);

        MerchantNameNormalizer.NormalizeDetectionKey(key, description: null).Should().Be(key);
    }

    [Theory]
    [MemberData(nameof(MerchantShapes))]
    public void Normalize_IsAFixedPointOverItsOwnOutput(string? raw)
    {
        var key = MerchantNameNormalizer.Normalize(raw);

        MerchantNameNormalizer.Normalize(key).Should().Be(key);
    }

    [Fact]
    public void NormalizeDetectionKey_IsAFixedPointOverEveryCombinationOfWhatItStrips()
    {
        // The hand-picked shapes above are the ones the book carries; this walks every
        // three-token combination of the fragments normalization actually reacts to, so a future
        // clause cannot pass the named cases while breaking the property in general.
        string[] fragments =
        [
            "netflix", "NETFLIX", ".com", ".co", " 12", "-4567", "*", "#", "  ", "\t", "paypal*",
            "anthropic", "MOBI TOP-UP 0857860057", "Mobile Top-Up 0057", "mobile top-up 0057", "",
        ];

        var moved = new List<string>();
        foreach (var a in fragments)
        {
            foreach (var b in fragments)
            {
                foreach (var c in fragments)
                {
                    var key = MerchantNameNormalizer.NormalizeDetectionKey(a + b + c, description: null);
                    if (MerchantNameNormalizer.NormalizeDetectionKey(key, description: null) != key)
                        moved.Add($"{a + b + c} -> {key}");
                }
            }
        }

        moved.Should().BeEmpty();
    }

    [Theory]
    [InlineData("mobile top-up 0057")]
    [InlineData("Mobile Top-Up 0057")]
    public void NormalizeDetectionKey_MobileTopUpKey_ReDerivesToItself(string key)
    {
        // The case that used to move: the trailing four digits read as statement noise and the
        // key collapsed to "mobile top-up", losing the number that makes it per-line. Spelling
        // does not matter — one line must not key two ways.
        MerchantNameNormalizer.NormalizeDetectionKey(key, description: null)
            .Should().Be("mobile top-up 0057");
    }

    [Fact]
    public void Normalize_DomainSuffixBehindTrailingDigits_IsStillStripped()
    {
        // One pass left "netflix.com": the suffix only reaches the end of the string after the
        // trailing digits go, so the same charge keyed two ways depending on the spelling.
        MerchantNameNormalizer.Normalize("netflix.com 12").Should().Be("netflix");
    }
}
