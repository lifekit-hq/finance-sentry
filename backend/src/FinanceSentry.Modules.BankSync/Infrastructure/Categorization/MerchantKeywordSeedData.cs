namespace FinanceSentry.Modules.BankSync.Infrastructure.Categorization;

using FinanceSentry.Core.Domain;

/// <summary>
/// Seed rows for the <c>merchant_keywords</c> bridge table — a lowercase description
/// substring mapped to a Plaid PFC primary key. Used as a fallback for providers that
/// return no structured category (e.g. TrueLayer for many EU banks), where the merchant
/// name is only present inside the free-text description.
///
/// This is a starter set, not an authority: rows are runtime-editable, longer keywords win
/// over shorter ones, and the canonical taxonomy remains Plaid's (see <see cref="CategoryKeys"/>).
/// </summary>
public static class MerchantKeywordSeedData
{
    public static readonly IReadOnlyList<(string Keyword, string CategoryKey)> Keywords =
    [
        // --- Groceries & supermarkets (FOOD_AND_DRINK) ---
        ("lidl", CategoryKeys.FoodAndDrink),
        ("tesco", CategoryKeys.FoodAndDrink),
        ("aldi", CategoryKeys.FoodAndDrink),
        ("supervalu", CategoryKeys.FoodAndDrink),
        ("dunnes", CategoryKeys.FoodAndDrink),
        ("centra", CategoryKeys.FoodAndDrink),
        ("eurospar", CategoryKeys.FoodAndDrink),
        ("spar", CategoryKeys.FoodAndDrink),
        ("costcutter", CategoryKeys.FoodAndDrink),
        ("iceland", CategoryKeys.FoodAndDrink),
        ("marks & spencer", CategoryKeys.FoodAndDrink),
        ("m&s", CategoryKeys.FoodAndDrink),
        ("grocery", CategoryKeys.FoodAndDrink),
        ("supermarket", CategoryKeys.FoodAndDrink),

        // --- Restaurants, cafes, takeaway (FOOD_AND_DRINK) ---
        ("mcdonald", CategoryKeys.FoodAndDrink),
        ("burger king", CategoryKeys.FoodAndDrink),
        ("kfc", CategoryKeys.FoodAndDrink),
        ("subway", CategoryKeys.FoodAndDrink),
        ("starbucks", CategoryKeys.FoodAndDrink),
        ("costa coffee", CategoryKeys.FoodAndDrink),
        ("insomnia", CategoryKeys.FoodAndDrink),
        ("coffee", CategoryKeys.FoodAndDrink),
        ("restaurant", CategoryKeys.FoodAndDrink),
        ("pizza", CategoryKeys.FoodAndDrink),
        ("domino", CategoryKeys.FoodAndDrink),
        ("nando", CategoryKeys.FoodAndDrink),
        ("thai", CategoryKeys.FoodAndDrink),
        ("sushi", CategoryKeys.FoodAndDrink),
        ("zakura", CategoryKeys.FoodAndDrink),
        ("papi chulo", CategoryKeys.FoodAndDrink),
        ("camile", CategoryKeys.FoodAndDrink),
        ("deliveroo", CategoryKeys.FoodAndDrink),
        ("just eat", CategoryKeys.FoodAndDrink),
        ("uber eats", CategoryKeys.FoodAndDrink),
        ("bar ", CategoryKeys.FoodAndDrink),
        ("pub", CategoryKeys.FoodAndDrink),

        // --- General merchandise / shopping (GENERAL_MERCHANDISE) ---
        ("amazon", CategoryKeys.GeneralMerchandise),
        ("ebay", CategoryKeys.GeneralMerchandise),
        ("aliexpress", CategoryKeys.GeneralMerchandise),
        ("shein", CategoryKeys.GeneralMerchandise),
        ("temu", CategoryKeys.GeneralMerchandise),
        ("argos", CategoryKeys.GeneralMerchandise),
        ("penneys", CategoryKeys.GeneralMerchandise),
        ("primark", CategoryKeys.GeneralMerchandise),
        ("zara", CategoryKeys.GeneralMerchandise),
        ("h&m", CategoryKeys.GeneralMerchandise),
        ("tk maxx", CategoryKeys.GeneralMerchandise),
        ("currys", CategoryKeys.GeneralMerchandise),
        ("harvey norman", CategoryKeys.GeneralMerchandise),
        ("smyths", CategoryKeys.GeneralMerchandise),
        ("decathlon", CategoryKeys.GeneralMerchandise),
        ("ikea", CategoryKeys.GeneralMerchandise),

        // --- Transport & fuel (TRANSPORTATION) ---
        ("leap card", CategoryKeys.Transportation),
        ("dublin bus", CategoryKeys.Transportation),
        ("bus eireann", CategoryKeys.Transportation),
        ("irish rail", CategoryKeys.Transportation),
        ("luas", CategoryKeys.Transportation),
        ("free now", CategoryKeys.Transportation),
        ("freenow", CategoryKeys.Transportation),
        ("bolt.eu", CategoryKeys.Transportation),
        ("uber", CategoryKeys.Transportation),
        ("taxi", CategoryKeys.Transportation),
        ("circle k", CategoryKeys.Transportation),
        ("applegreen", CategoryKeys.Transportation),
        ("maxol", CategoryKeys.Transportation),
        ("texaco", CategoryKeys.Transportation),
        ("esso", CategoryKeys.Transportation),
        ("petrol", CategoryKeys.Transportation),
        ("fuel", CategoryKeys.Transportation),
        ("parking", CategoryKeys.Transportation),
        ("eflow", CategoryKeys.Transportation),
        ("toll", CategoryKeys.Transportation),

        // --- Travel (TRAVEL) ---
        ("booking.com", CategoryKeys.Travel),
        ("airbnb", CategoryKeys.Travel),
        ("ryanair", CategoryKeys.Travel),
        ("aer lingus", CategoryKeys.Travel),
        ("expedia", CategoryKeys.Travel),
        ("esky", CategoryKeys.Travel),
        ("kiwi.com", CategoryKeys.Travel),
        ("skyscanner", CategoryKeys.Travel),
        ("opodo", CategoryKeys.Travel),
        ("lastminute", CategoryKeys.Travel),
        ("easyjet", CategoryKeys.Travel),
        ("wizz air", CategoryKeys.Travel),
        ("hotel", CategoryKeys.Travel),
        ("hostel", CategoryKeys.Travel),

        // --- Bills & utilities / telecom / hosting (RENT_AND_UTILITIES) ---
        ("electric ireland", CategoryKeys.RentAndUtilities),
        ("bord gais", CategoryKeys.RentAndUtilities),
        ("sse airtricity", CategoryKeys.RentAndUtilities),
        ("virgin media", CategoryKeys.RentAndUtilities),
        ("vodafone", CategoryKeys.RentAndUtilities),
        ("gomo", CategoryKeys.RentAndUtilities),
        ("eir ", CategoryKeys.RentAndUtilities),
        ("three ", CategoryKeys.RentAndUtilities),
        ("sky ", CategoryKeys.RentAndUtilities),

        // --- Entertainment / streaming (ENTERTAINMENT) ---
        ("netflix", CategoryKeys.Entertainment),
        ("spotify", CategoryKeys.Entertainment),
        ("disney", CategoryKeys.Entertainment),
        ("youtube", CategoryKeys.Entertainment),
        ("prime video", CategoryKeys.Entertainment),
        ("cinema", CategoryKeys.Entertainment),
        ("odeon", CategoryKeys.Entertainment),
        ("steam games", CategoryKeys.Entertainment),
        ("steampowered", CategoryKeys.Entertainment),
        ("playstation", CategoryKeys.Entertainment),
        ("xbox", CategoryKeys.Entertainment),
        ("nintendo", CategoryKeys.Entertainment),
        ("twitch", CategoryKeys.Entertainment),
        ("patreon", CategoryKeys.Entertainment),

        // --- Digital / SaaS / dev services (GENERAL_SERVICES) ---
        ("openai", CategoryKeys.GeneralServices),
        ("chatgpt", CategoryKeys.GeneralServices),
        ("anthropic", CategoryKeys.GeneralServices),
        ("claude", CategoryKeys.GeneralServices),
        ("github", CategoryKeys.GeneralServices),
        ("microsoft", CategoryKeys.GeneralServices),
        ("google ", CategoryKeys.GeneralServices),
        ("adobe", CategoryKeys.GeneralServices),
        ("notion", CategoryKeys.GeneralServices),
        ("figma", CategoryKeys.GeneralServices),
        ("digitalocean", CategoryKeys.GeneralServices),
        ("netcup", CategoryKeys.GeneralServices),
        ("namecheap", CategoryKeys.GeneralServices),
        ("godaddy", CategoryKeys.GeneralServices),
        ("cloudflare", CategoryKeys.GeneralServices),
        ("aws", CategoryKeys.GeneralServices),

        // --- Medical & pharmacy (MEDICAL) ---
        ("pharmacy", CategoryKeys.Medical),
        ("chemist", CategoryKeys.Medical),
        ("boots", CategoryKeys.Medical),
        ("lloyds pharmacy", CategoryKeys.Medical),
        ("mccabes", CategoryKeys.Medical),
        ("hickeys", CategoryKeys.Medical),
        ("medical", CategoryKeys.Medical),
        ("clinic", CategoryKeys.Medical),
        ("dental", CategoryKeys.Medical),
        ("doctor", CategoryKeys.Medical),
        ("hospital", CategoryKeys.Medical),

        // --- Personal care (PERSONAL_CARE) ---
        ("hair", CategoryKeys.PersonalCare),
        ("barber", CategoryKeys.PersonalCare),
        ("salon", CategoryKeys.PersonalCare),
        ("beauty", CategoryKeys.PersonalCare),
        ("spa ", CategoryKeys.PersonalCare),

        // --- Home improvement / hardware (HOME_IMPROVEMENT) ---
        ("woodie", CategoryKeys.HomeImprovement),
        ("b&q", CategoryKeys.HomeImprovement),
        ("homebase", CategoryKeys.HomeImprovement),
        ("hardware", CategoryKeys.HomeImprovement),

        // --- Second-pass coverage (from observed uncategorized TrueLayer merchants) ---
        ("apple.com", CategoryKeys.GeneralServices),
        ("hetzner", CategoryKeys.GeneralServices),
        ("top-up", CategoryKeys.RentAndUtilities),   // mobile top-up
        ("gymbeam", CategoryKeys.GeneralMerchandise), // sports-nutrition shop; must beat "gym"
        ("gym", CategoryKeys.Medical),
        ("cafe", CategoryKeys.FoodAndDrink),
        ("boojum", CategoryKeys.FoodAndDrink),
        ("beshoffs", CategoryKeys.FoodAndDrink),
        ("dublinbikes", CategoryKeys.Transportation),
        ("bleeper", CategoryKeys.Transportation),
        ("ticketmaster", CategoryKeys.Entertainment), // longer than "klarna", so it wins
        ("klarna", CategoryKeys.GeneralMerchandise),
        ("carrolls irish", CategoryKeys.GeneralMerchandise),
        ("fee-qtr", CategoryKeys.BankFees),

        // --- TrueLayer description fallbacks: common IE merchants that arrive with no MCC ---
        ("popeyes", CategoryKeys.FoodAndDrink),
        ("cineworld", CategoryKeys.Entertainment),
        ("free-now", CategoryKeys.Transportation),
        ("veolia", CategoryKeys.Transportation),
        ("dublin express", CategoryKeys.Transportation),
        ("lego", CategoryKeys.GeneralMerchandise),
        ("makeup", CategoryKeys.PersonalCare),
        ("clubwise", CategoryKeys.Medical),

        // --- Monobank installment / fee wordings (#581) ---
        // These charges carry the wire-transfer MCC 4829 (or the misleading card-service
        // wording), so without a keyword they land in TRANSFER_OUT and vanish from spending.
        // Present on the VPS as runtime rows since the August audit; seeded here so fresh
        // databases classify them the same way.
        ("погашення", CategoryKeys.LoanPayments),
        ("щомісячний платіж", CategoryKeys.LoanPayments),
        ("monomarket", CategoryKeys.LoanPayments),
        ("платіж pandora", CategoryKeys.LoanPayments),
        ("платинової картки", CategoryKeys.BankFees),
    ];
}
