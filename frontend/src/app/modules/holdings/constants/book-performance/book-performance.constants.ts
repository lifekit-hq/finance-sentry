/**
 * The brokerage book's return is measured in USD (C4).
 */
export const BOOK_TWR_CURRENCY = 'USD';

/** The book TWR runs on the cash-inclusive brokerage sleeve (C3). */
export const BOOK_TWR_SCOPE = 'sleeve:brokerage';

/** The book TWR is a time-weighted return (M3). */
export const BOOK_TWR_METHOD = 'TWR';

/** A return arrives as a fraction; the card reads it in percentage points. */
export const PERCENT_POINTS = 100;

/** A return reads to one decimal place: "+4.2%". */
export const RETURN_PERCENT_DIGITS = 1;

/** The 3M period of `GET /radar/book-performance` (the server's `BookPerformancePeriod.ThreeMonths`), the card's second figure. */
export const BOOK_PERFORMANCE_THREE_MONTHS = 2;

/**
 * The basis under the figures (money-semantics §10): the book's cash-inclusive time-weighted return against
 * SPY's adjusted-close (total) return over the same dates. Every return figure on the card sits above it.
 */
export const BOOK_VS_SPY_BASIS_NOTE =
  'Basis: cash-inclusive time-weighted return, brokerage sleeve; benchmark: SPY adjusted-close (total return) over the same dates.';

/** The track-record scopes whose flag raises the alert (money-semantics §10); thesis rows carry none. */
export const SUSTAINED_FLAG_SCOPES: readonly string[] = ['Book', 'Sleeve'];
