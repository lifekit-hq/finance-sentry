/**
 * Series step per spending-category key. Steps 1 and 6 are not used: in the default theme they
 * match the Brokerage (`asset-equity`) and Crypto (`asset-crypto`) colours. Six steps for fifteen
 * keys means rare categories share a step with common ones.
 */
export const CATEGORY_SERIES_STEP: Readonly<Record<string, number>> = {
  FOOD_AND_DRINK: 2,
  GENERAL_MERCHANDISE: 3,
  RENT_AND_UTILITIES: 4,
  TRANSPORTATION: 5,
  ENTERTAINMENT: 7,
  TRAVEL: 8,
  HOME_IMPROVEMENT: 4,
  MEDICAL: 5,
  PERSONAL_CARE: 7,
  GENERAL_SERVICES: 8,
  GOVERNMENT_AND_NON_PROFIT: 2,
  LOAN_PAYMENTS: 3,
  BANK_FEES: 5,
  FAMILY_SUPPORT: 7,
  UNCATEGORIZED: 8,
};

/** Steps an unmapped category key hashes into; the asset-matching steps 1 and 6 are excluded. */
export const CATEGORY_FALLBACK_STEPS: readonly number[] = Object.values(
  CATEGORY_SERIES_STEP
).filter((step, i, all) => all.indexOf(step) === i);

export const CATEGORY_HASH_MULTIPLIER = 31;
