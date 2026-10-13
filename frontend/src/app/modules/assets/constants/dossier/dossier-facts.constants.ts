/** Why a fundamentals cell is empty: a reason flag, never a zero. */
export const FUNDAMENTAL_REASONS = {
  notReported: 'not reported',
  noEstimate: 'no forward estimate',
  noEarnings: 'no positive earnings',
  noDividend: 'no dividend',
  noHistory: 'no price history',
  noTarget: 'no analyst target',
} as const;

/** Market-cap suffixes, largest first. */
export const MARKET_CAP_UNITS: readonly {size: number; suffix: string}[] = [
  {size: 1e12, suffix: 'T'},
  {size: 1e9, suffix: 'B'},
  {size: 1e6, suffix: 'M'},
];

/** No free source carries holder data, so the section says so rather than guess. */
export const OWNERSHIP_NOTE = 'Not available - no free source for holder data';
