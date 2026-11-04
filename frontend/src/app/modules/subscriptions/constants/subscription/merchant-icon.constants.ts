import {type IconName} from '@lifekit-hq/ui';

/**
 * The icon a row shows when no curated merchant matched it: one per spending-category group (the
 * category a row carries is derived from the merchant's MCC). A category not listed, or none,
 * keeps the merchant's initial.
 */
export const MERCHANT_CATEGORY_ICONS: Readonly<Record<string, IconName>> = {
  FOOD_AND_DRINK: 'Utensils',
  GENERAL_MERCHANDISE: 'ShoppingBag',
  RENT_AND_UTILITIES: 'Lightbulb',
  TRANSPORTATION: 'Car',
  ENTERTAINMENT: 'Clapperboard',
  TRAVEL: 'Plane',
  HOME_IMPROVEMENT: 'Hammer',
  MEDICAL: 'HeartPulse',
  PERSONAL_CARE: 'Sparkles',
  GENERAL_SERVICES: 'Wrench',
  GOVERNMENT_AND_NON_PROFIT: 'Landmark',
  LOAN_PAYMENTS: 'Banknote',
  BANK_FEES: 'Receipt',
  FAMILY_SUPPORT: 'Users',
};
