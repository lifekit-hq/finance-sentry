/** The slice of `research/quotes` the holdings page reads. */
export interface QuoteDto {
  ticker: string;
  resolvedTicker: Nullable<string>;
  price: number;
  previousClose: Nullable<number>;
  /** Day change in percent against the previous close; null when no previous close is known. */
  changePct: Nullable<number>;
  currency: string;
}
