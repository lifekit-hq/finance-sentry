namespace FinanceSentry.Modules.BankSync.Infrastructure.Categorization;

using FinanceSentry.Core.Domain;

/// <summary>
/// The single piece of category glue we own. Maps an ISO 18245 Merchant Category Code
/// to a Plaid PFC primary key using the standard's numeric ranges, with a small set of
/// per-code overrides for well-known exceptions (e.g. pharmacies inside the retail range).
///
/// This exists only because card networks (Monobank) return a raw MCC number and no
/// category name, and no public MCC-to-personal-finance-category crosswalk exists. The
/// output is written into the editable <c>mcc_categories</c> table at seed time, so any
/// individual decision here can be corrected with a row edit without touching code.
/// </summary>
public static class MccRangeClassifier
{
    // Specific codes whose category differs from their numeric neighbourhood.
    private static readonly IReadOnlyDictionary<int, string> Overrides = new Dictionary<int, string>
    {
        [4119] = CategoryKeys.Medical,             // Ambulance services
        [4829] = CategoryKeys.TransferOut,         // Wire transfer / money orders
        [5047] = CategoryKeys.Medical,             // Medical/dental/ophthalmic equipment
        [5122] = CategoryKeys.Medical,             // Drugs, proprietaries
        [5262] = CategoryKeys.GeneralMerchandise,  // Marketplaces (online)
        [5541] = CategoryKeys.Transportation,      // Service stations (gas)
        [5542] = CategoryKeys.Transportation,      // Automated fuel dispensers
        [5811] = CategoryKeys.FoodAndDrink,        // Caterers
        [5912] = CategoryKeys.Medical,             // Drug stores / pharmacies
        [5921] = CategoryKeys.FoodAndDrink,        // Package stores — beer, wine, liquor
        [5975] = CategoryKeys.Medical,             // Hearing aids
        [5976] = CategoryKeys.Medical,             // Orthopedic goods
        [5977] = CategoryKeys.PersonalCare,        // Cosmetic stores
        [5983] = CategoryKeys.RentAndUtilities,    // Fuel dealers (heating oil)
        [6010] = CategoryKeys.TransferOut,         // Financial institutions — manual cash
        [6011] = CategoryKeys.TransferOut,         // Financial institutions — ATM cash
        [6012] = CategoryKeys.GeneralServices,     // Financial institutions — merchandise
        [6051] = CategoryKeys.TransferOut,         // Quasi-cash / crypto
        [6211] = CategoryKeys.TransferOut,         // Securities — brokers/dealers
        [6300] = CategoryKeys.GeneralServices,     // Insurance
        [6513] = CategoryKeys.RentAndUtilities,    // Real estate agents — rentals
        [7230] = CategoryKeys.PersonalCare,        // Beauty & barber shops
        [7297] = CategoryKeys.PersonalCare,        // Massage parlors
        [7298] = CategoryKeys.PersonalCare,        // Health & beauty spas
        [7299] = CategoryKeys.PersonalCare,        // Misc personal services
        [7512] = CategoryKeys.Travel,              // Automobile rental agency
        [7519] = CategoryKeys.Travel,              // Motor home / RV rental
        [7641] = CategoryKeys.HomeImprovement,     // Furniture reupholstery/repair
        [8398] = CategoryKeys.GovernmentAndNonProfit, // Charitable organizations
        [8641] = CategoryKeys.GovernmentAndNonProfit, // Civic/social/fraternal associations
        [8651] = CategoryKeys.GovernmentAndNonProfit, // Political organizations
        [8661] = CategoryKeys.GovernmentAndNonProfit, // Religious organizations
    };

    // Ordered ISO 18245 ranges. First containing range wins (after overrides).
    private static readonly IReadOnlyList<(int Lo, int Hi, string Key)> Ranges =
    [
        (0700, 0999, CategoryKeys.GeneralServices),          // Agricultural / veterinary / horticultural
        (1500, 1799, CategoryKeys.HomeImprovement),          // Contractors, construction
        (2000, 2999, CategoryKeys.GeneralServices),          // Printing, specialty trade
        (3000, 3299, CategoryKeys.Travel),                   // Airlines
        (3300, 3499, CategoryKeys.Travel),                   // Car rental
        (3500, 3999, CategoryKeys.Travel),                   // Hotels / lodging
        (4011, 4013, CategoryKeys.Transportation),           // Railroads
        (4111, 4131, CategoryKeys.Transportation),           // Local transit, taxis, ferries, buses
        (4411, 4411, CategoryKeys.Travel),                   // Cruise lines
        (4457, 4468, CategoryKeys.Travel),                   // Boat rentals / marinas
        (4511, 4511, CategoryKeys.Travel),                   // Airlines (air carriers)
        (4582, 4582, CategoryKeys.Travel),                   // Airports / flying fields
        (4722, 4723, CategoryKeys.Travel),                   // Travel agencies / tour operators
        (4214, 4225, CategoryKeys.GeneralServices),          // Freight, courier, warehousing & storage
        (4784, 4789, CategoryKeys.Transportation),           // Tolls, transportation services
        (4812, 4900, CategoryKeys.RentAndUtilities),         // Telecom, cable, utilities
        (5013, 5199, CategoryKeys.GeneralMerchandise),       // Wholesale / durable goods
        (5200, 5271, CategoryKeys.HomeImprovement),          // Home supply, hardware, garden
        (5300, 5399, CategoryKeys.GeneralMerchandise),       // Wholesale clubs, discount, variety
        (5411, 5499, CategoryKeys.FoodAndDrink),             // Grocery & food stores
        (5511, 5599, CategoryKeys.Transportation),           // Automotive dealers, parts, gas
        (5611, 5699, CategoryKeys.GeneralMerchandise),       // Clothing & apparel
        (5712, 5722, CategoryKeys.HomeImprovement),          // Furniture, home furnishings, appliances
        (5731, 5735, CategoryKeys.GeneralMerchandise),       // Electronics, music, software
        (5811, 5814, CategoryKeys.FoodAndDrink),             // Eating & drinking places
        (5815, 5816, CategoryKeys.Entertainment),            // Digital goods: media, books, music, games
        (5817, 5817, CategoryKeys.GeneralServices),          // Digital goods: applications (excl. games)
        (5818, 5818, CategoryKeys.GeneralMerchandise),       // Digital goods: large digital goods merchant
        (5832, 5832, CategoryKeys.GeneralMerchandise),       // Antique shops
        (5900, 5999, CategoryKeys.GeneralMerchandise),       // Misc retail (see overrides)
        (6000, 6999, CategoryKeys.GeneralServices),          // Financial (see overrides)
        (7011, 7033, CategoryKeys.Travel),                   // Lodging, campgrounds
        (7210, 7299, CategoryKeys.GeneralServices),          // Laundry & personal services (see overrides)
        (7300, 7399, CategoryKeys.GeneralServices),          // Business services
        (7511, 7549, CategoryKeys.Transportation),           // Auto services, parking, car wash
        (7622, 7699, CategoryKeys.GeneralServices),          // Repair services
        (7800, 7999, CategoryKeys.Entertainment),            // Amusement & recreation
        (8000, 8099, CategoryKeys.Medical),                  // Medical & health services
        (8100, 8299, CategoryKeys.GeneralServices),          // Legal, education
        (8300, 8399, CategoryKeys.GovernmentAndNonProfit),   // Membership / charitable
        (8400, 8999, CategoryKeys.GeneralServices),          // Professional / membership services
        (9000, 9999, CategoryKeys.GovernmentAndNonProfit),   // Government services
    ];

    /// <summary>
    /// Classifies an MCC into a canonical category key, or
    /// <see cref="CategoryKeys.Uncategorized"/> when no rule matches.
    /// </summary>
    public static string Classify(int mcc)
    {
        if (Overrides.TryGetValue(mcc, out var key))
            return key;

        foreach (var (lo, hi, rangeKey) in Ranges)
        {
            if (mcc >= lo && mcc <= hi)
                return rangeKey;
        }

        return CategoryKeys.Uncategorized;
    }
}
