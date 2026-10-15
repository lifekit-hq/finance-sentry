import {type ErrorMessagesMap} from '@lifekit-hq/core';

/**
 * Words for the backend's typed `HeadlineReasonCode`s (finance-framework §3.6), keyed by code and
 * resolved through `ErrorMessageService` like every other code. Every backend code has an entry
 * (`headline-reasons.registry.spec.ts` pins it); an unknown code falls back to a generic line.
 */
export const HEADLINE_REASON_MESSAGES: ErrorMessagesMap = {
  STALE_SOURCE:
    'A connected source could not be refreshed, so its last known values are used. They may be out of date until it syncs again.',
  BACKFILLED_ESTIMATE:
    'Part of this history was reconstructed from what is known today, not recorded on the day.',
  FX_TODAY_RATE:
    "Some amounts were converted at today's exchange rate instead of the rate on their own date.",
  FX_UNKNOWN_1TO1:
    'A currency has no exchange rate yet, so its amounts were counted one-to-one. The total may be off.',
  NO_SPLIT: 'The cash and invested split could not be measured for this period.',
  SCOPE_CHANGED:
    'Accounts were added or removed during this period, so a percentage would not mean much. The amount is still correct.',
  SHORT_HISTORY: 'There is not enough history yet to measure this period.',
  ZERO_BASE: 'The starting value was zero, so there is no percentage to show.',
  NEGATIVE_BASE: 'The starting value was negative, so a percentage of it would be misleading.',
  NOT_MEASURABLE: 'This figure could not be measured from the data available.',
  UNKNOWN_FLOW: 'Some money movements in this period could not be classified.',
  UNMATCHED_TRANSFER: 'A transfer between your accounts has no matching entry on the other side.',
  NON_CASH_INCLUDED: 'Items that are not cash are counted in this cash figure.',
  PRICE_CARRIED_FORWARD: 'A price from an earlier day is used where today’s is missing.',
  RESIDUAL_ABOVE_TOLERANCE:
    'The change in net worth is not fully explained by known movements and price changes.',
  NO_COST_BASIS:
    'None of these holdings has a known purchase cost, so a gain against cost cannot be measured.',
};
