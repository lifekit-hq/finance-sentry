import {type HeadlineBadgeKind} from '../../models/headline/headline.model';

/** The reason codes the backend's `HeadlineReasonCode` carries; every one has a registry entry. */
export const HEADLINE_REASON_CODES = [
  'STALE_SOURCE',
  'BACKFILLED_ESTIMATE',
  'FX_TODAY_RATE',
  'FX_UNKNOWN_1TO1',
  'NO_SPLIT',
  'SCOPE_CHANGED',
  'SHORT_HISTORY',
  'ZERO_BASE',
  'NEGATIVE_BASE',
  'NOT_MEASURABLE',
  'UNKNOWN_FLOW',
  'UNMATCHED_TRANSFER',
  'NON_CASH_INCLUDED',
  'PRICE_CARRIED_FORWARD',
  'RESIDUAL_ABOVE_TOLERANCE',
  'NO_COST_BASIS',
  'ESTIMATED_VALUE',
] as const;

export const STALE_SOURCE_CODE = 'STALE_SOURCE';

/** Shown for a reason code the registry has no words for yet, so a new backend code never breaks the sheet. */
export const HEADLINE_REASON_FALLBACK =
  'Something about the data behind this figure is less certain than usual.';

export const HEADLINE_BADGE_LABEL: Readonly<Record<HeadlineBadgeKind, string>> = {
  estimated: 'Estimated',
  stale: 'Stale',
  partial: 'Partial',
  unavailable: 'Unavailable',
};

/** The sentence that opens the reason sheet, per badge. */
export const HEADLINE_BADGE_SUMMARY: Readonly<Record<HeadlineBadgeKind, string>> = {
  estimated: 'This figure is an estimate, not a direct measurement.',
  stale: 'Some of the data behind this figure is out of date.',
  partial: 'This figure is missing some of the data it needs.',
  unavailable: 'This figure cannot be shown right now.',
};

/** What a figure with no amount renders as; never 0 or 0%. */
export const HEADLINE_UNAVAILABLE_TEXT = 'N/A';

export const HEADLINE_REASON_SHEET_WIDTH = '24rem';
