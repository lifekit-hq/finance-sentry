/** The sum the growth chart puts in on day one. */
export const GROWTH_OF_TEN_THOUSAND_AMOUNT = 10_000;

// shortcut: the sum is in USD because the returns are measured in USD (C4); show it in the base currency
// once the book TWR is measured in it.
export const GROWTH_OF_TEN_THOUSAND_CURRENCY = 'USD';

/** The book TWR runs on the cash-inclusive brokerage sleeve (C3). */
export const BOOK_TWR_SCOPE = 'sleeve:brokerage';

/** The book TWR is a time-weighted return (M3). */
export const BOOK_TWR_METHOD = 'TWR';

/** A return arrives as a fraction; the card reads it in percentage points. */
export const PERCENT_POINTS = 100;

/** A return reads to one decimal place: "+4.2%". */
export const RETURN_PERCENT_DIGITS = 1;
