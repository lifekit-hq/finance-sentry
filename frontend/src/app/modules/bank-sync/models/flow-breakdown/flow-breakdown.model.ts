/**
 * Classification bucket a transaction landed in for the month's flow figures — the audit
 * labels behind the dashboard tiles. Mirrors `FlowBuckets` on the backend.
 */
export type FlowBucket =
  | 'income'
  | 'spending'
  | 'invested'
  | 'investment-return'
  | 'excluded-pair'
  | 'excluded-routing'
  | 'excluded-transfer';

export interface FlowBreakdownItem {
  transactionId: string;
  accountId: string;
  bankName: string;
  accountLast4: string;
  currency: string;
  amount: number;
  amountUsd: number;
  date: string;
  description: string;
  merchantName: Nullable<string>;
  category: Nullable<string>;
  direction: 'in' | 'out';
  bucket: FlowBucket;
  counterpartyName: Nullable<string>;
  flowRole: Nullable<string>;
}

export interface FlowBreakdown {
  /** `yyyy-MM`, or empty for a day-range breakdown. */
  month: string;
  items: FlowBreakdownItem[];
}

/**
 * The UTC calendar days a dashboard window drills into. `from` is null for all-time (no lower
 * bound); `months` is how much history the backend loads around it for transfer-pair
 * detection, the same value the dashboard asked for.
 */
export interface FlowBreakdownRange {
  from: Nullable<string>;
  to: string;
  months: number;
}

/** What the page is asked to show: one month, or a dashboard window's day range. */
export type FlowBreakdownRequest =
  {kind: 'month'; month: string} | ({kind: 'range'} & FlowBreakdownRange);
