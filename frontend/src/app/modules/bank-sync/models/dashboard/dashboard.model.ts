import {
  type HeadlineQuality,
  type MaybeHeadline,
} from '../../../../shared/models/headline/headline.model';

export type HistoryRange = '1w' | 'mtd' | '1m' | '3m' | 'ytd' | '1y' | 'all';

export interface NetWorthSnapshotDto {
  snapshotDate: string;
  bankingTotal: number;
  brokerageTotal: number;
  cryptoTotal: number;
  totalNetWorth: number;
  currency: string;
  /** Comma-separated sleeves ('banking','brokerage','crypto') carried forward because their
   * feed was stale that day — the value is estimated, not measured. Null when all fresh. */
  staleSleeves?: string | null;
  /** Cash / invested split of `totalNetWorth` (cash is the remainder, so the three add up to it).
   * All three are null together on a day with no split — before the backfill window, or a
   * source that was stale at capture. Null means "no split", never zero. */
  cashTotal?: Nullable<number>;
  brokerageInvested?: Nullable<number>;
  cryptoInvested?: Nullable<number>;
  /** The day's sources: the three sleeves, then any other source. A source the snapshot carried
   * forward or could not read carries `STALE_SOURCE` in `reasons`. Absent on an older API build. */
  sources?: Nullable<NetWorthSourceDto[]>;
}

/** One source's part of a history row: the figure counted for it, its health and typed reasons. */
export interface NetWorthSourceDto {
  sourceId: string;
  kind: string;
  /** In the row's currency; null only for a source outside the three sleeves whose read failed. */
  total: Nullable<number>;
  health: HeadlineQuality;
  reasons: string[];
}

/** A snapshot's cash / invested split, once all three parts are known. */
export interface NetWorthSplit {
  cash: number;
  brokerageInvested: number;
  cryptoInvested: number;
  /** `brokerageInvested + cryptoInvested`. */
  invested: number;
}

/** An account or connection that entered or left the book inside the returned window. */
export interface NetWorthScopeChangeDto {
  source: string;
  kind: 'linked' | 'removed';
  /** ISO date (YYYY-MM-DD, UTC). */
  at: string;
}

export interface NetWorthHistoryResponse {
  snapshots: NetWorthSnapshotDto[];
  hasHistory: boolean;
  /** Absent from older API builds; treat as none. */
  scopeChanges?: NetWorthScopeChangeDto[];
  /** The window's change as a typed record: quality, reasons, and the withheld percent's why. */
  changeHeadline?: MaybeHeadline;
}

export interface MonthlyFlow {
  month: string;
  currency: string;
  inflow: number;
  outflow: number;
  net: number;
  inflowUsd: number;
  outflowUsd: number;
  netUsd: number;
  /** Gross family-support expense (per direction, never netted against family income),
   * included in outflowUsd; zero when no counterparty expense. */
  familySupportOutflowUsd?: number;
  /**
   * Net movement routed to an investment venue. Deliberately NOT part of outflowUsd —
   * investing is not spending — so it is carved out of what would otherwise read as
   * cash simply kept.
   */
  investedOutflowUsd?: number;
}

export interface CategoryStat {
  category: string;
  totalSpend: number;
  percentOfTotal: number;
}

export interface DashboardData {
  aggregatedBalance: Record<string, number>;
  totalNetWorthUsd: number;
  accountCount: number;
  accountsByType: Record<string, number>;
  monthlyFlow: MonthlyFlow[];
  /**
   * Flow for exactly the days of a day-resolution window (1W, MTD, 1M), still keyed by month
   * (so the first bucket is partial). Present only when the request carried `windowFrom`;
   * `monthlyFlow` stays the whole-month history the charts plot.
   */
  windowFlow?: MonthlyFlow[];
  topCategories: CategoryStat[];
  lastSyncTimestamp: Nullable<string>;
  /** Currency every `…Usd` figure and category total is expressed in — the profile's base
   * currency (the field names keep their historical suffix). */
  baseCurrency?: string;
  /** Cash and invested split of the book (`IBookFiguresService`), in `baseCurrency`. Cash nets
   * card debt, so it can be negative. Null when the book was not read in full. */
  cashUsd?: Nullable<number>;
  investedUsd?: Nullable<number>;
  /**
   * The same figures as typed records (finance-framework §3.6): the net worth, and the cash and
   * invested split lines. Absent from older API builds; their `quality` and `reasons` drive the
   * badge on each figure.
   */
  netWorthHeadline?: MaybeHeadline;
  cashHeadline?: MaybeHeadline;
  investedHeadline?: MaybeHeadline;
}
