namespace FinanceSentry.Modules.Subscriptions.Application.Icons;

/// <summary>
/// The starter set of <c>merchant_brands</c>: widely known services and retailers, nothing specific
/// to one person. <c>Domain</c> is the exact host that answers <c>/favicon.ico</c> itself (a host
/// that redirects elsewhere is refused by the fetcher), so a brand whose apex redirects to
/// <c>www</c> lists the <c>www</c> host. Rows are inserted when missing; an edit or a row an operator
/// adds is never overwritten.
/// </summary>
public static class MerchantBrandSeedData
{
    public sealed record Entry(string Keyword, string Domain);

    public static IReadOnlyList<Entry> Entries { get; } =
    [
        new("netflix", "www.netflix.com"),
        new("spotify", "open.spotify.com"),
        new("youtube", "www.youtube.com"),
        new("google", "www.google.com"),
        new("apple", "www.apple.com"),
        new("microsoft", "www.microsoft.com"),
        new("xbox", "www.xbox.com"),
        new("amazon", "www.amazon.com"),
        new("disney", "www.disneyplus.com"),
        new("adobe", "www.adobe.com"),
        new("github", "github.com"),
        new("claude", "claude.ai"),
        new("zoom", "zoom.us"),
        new("figma", "www.figma.com"),
        new("steam", "store.steampowered.com"),
        new("nvidia", "www.nvidia.com"),
        new("deezer", "www.deezer.com"),
        new("reddit", "www.reddit.com"),
        new("cloudflare", "www.cloudflare.com"),
        new("linkedin", "www.linkedin.com"),
        new("tiktok", "www.tiktok.com"),
        new("paypal", "www.paypal.com"),
        new("uber", "www.uber.com"),
        new("airbnb", "www.airbnb.com"),
        new("glovo", "glovoapp.com"),
        new("ryanair", "www.ryanair.com"),
        new("wizz air", "www.wizzair.com"),
        new("wizzair", "www.wizzair.com"),
        new("ikea", "www.ikea.com"),
        new("nike", "www.nike.com"),
        new("starbucks", "www.starbucks.com"),
        new("pandora", "www.pandora.net"),
        new("epicentr", "epicentrk.ua"),
        new("epicentrk", "epicentrk.ua"),
        new("comfy", "comfy.ua"),
        new("foxtrot", "www.foxtrot.com.ua"),
        new("olx", "www.olx.ua"),
        new("vodafone", "www.vodafone.ua"),
        new("lifecell", "www.lifecell.ua"),
        new("kyivstar", "kyivstar.ua"),
    ];
}
