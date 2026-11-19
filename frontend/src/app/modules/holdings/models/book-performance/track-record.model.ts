/**
 * The slice of `GET /research/theses/track-record` (money-semantics §10) the Book vs SPY card reads: the
 * stored sustained-underperformance flag of a book or sleeve row. The flag is computed by the weekly job;
 * the client never recomputes it.
 */
export interface TrackRecordRow {
  /** `Book`, `Sleeve` or `Thesis`. */
  scope: string;
  label: string;
  window: string;
  sustainedUnderperformance: boolean;
}

export interface TrackRecord {
  /** The rule the stored flags were computed under; its window is the one that raises the alert. */
  rule: {window: string};
  rows: TrackRecordRow[];
}
