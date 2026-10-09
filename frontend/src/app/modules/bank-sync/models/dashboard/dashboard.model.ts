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
}

export interface NetWorthHistoryResponse {
  snapshots: NetWorthSnapshotDto[];
  hasHistory: boolean;
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
}
