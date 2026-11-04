/** The backend raises its near-limit alert at 90% of the estimate; the "Near limit" tag matches it. */
export const BUDGET_NEAR_LIMIT_PCT = 90;
/**
 * Lines identified by the counterparty's role, not by a stored transaction category: no category filter on the
 * ledger matches their spend, so they link nowhere.
 */
export const COUNTERPARTY_ROLE_LINES: readonly string[] = ['HOUSEHOLD', 'FAMILY_SUPPORT'];
export const MIN_BUDGET_LIMIT = 0.01;
export const BUDGETS_LOAD_ERROR_MESSAGE = 'Failed to load the monthly estimates.';
