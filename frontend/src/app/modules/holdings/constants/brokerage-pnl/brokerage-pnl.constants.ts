/** A P&L percent reads to one decimal place: "+10.9%". */
export const PNL_PERCENT_DIGITS = 1;

/** A share of value arrives as a fraction; the note reads it in whole percent. */
export const COVERAGE_PERCENT = 100;

/** The most symbols a note spells out before it counts the rest. */
export const MAX_SYMBOLS_LISTED = 3;

export const BASIS_UNAVAILABLE = 'Basis unavailable';

/** What the denominator holds, said once under the row: the opening balance is market value, not money put in. */
export const CAPITAL_IN_WINDOW_DETAIL =
  'Capital in the window is the opening value, plus providers linked in the window, plus deposits less withdrawals. When the history opens after the account was funded, the opening value already includes earlier market gains.';
