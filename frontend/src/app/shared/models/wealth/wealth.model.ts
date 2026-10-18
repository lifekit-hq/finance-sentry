import {type AccountIdentity} from '../account-identity/account-identity.model';
import {type MaybeHeadline} from '../headline/headline.model';

export type AccountCategory = 'banking' | 'crypto' | 'brokerage' | 'other';

/**
 * `retrying`, `reconnect_needed` and `dormant` come from a connection's health, and only for a provider the
 * connection-health policy drives (`ConnectionHealth:ActiveProviders`); every other provider keeps the rest.
 */
export type SyncStatus =
  | 'synced'
  | 'syncing'
  | 'pending'
  | 'stale'
  | 'failed'
  | 'reauth_required'
  | 'retrying'
  | 'reconnect_needed'
  | 'dormant';

export type HiddenReason = 'empty' | 'small';

export interface AccountBalanceItem extends AccountIdentity {
  provider: string;
  category: AccountCategory;
  currentBalance: number;
  balanceInBaseCurrency: Nullable<number>;
  syncStatus: SyncStatus;
  lastSyncTimestamp: Nullable<string>;
  productType?: Nullable<string>;
  /** A position's value as its provider reported it (an Inzhur fund in UAH); absent when `currentBalance` is already native. */
  nativeCurrency?: Nullable<string>;
  nativeValue?: Nullable<number>;
  /** A position's full ticker (`GOOGL`, `BRK.B`); `accountNumberLast4` is cut to four characters. Absent on bank accounts. */
  symbol?: Nullable<string>;
  /** Why the list tucks this row away (display-only: it still counts in every total); absent when it stays visible. */
  hiddenReason?: Nullable<HiddenReason>;
}

/**
 * A physical card within an institution, grouping its per-currency sub-accounts.
 * Populated by the backend only for providers with a card product (Monobank
 * black/white/…); absent otherwise. Empty sub-accounts and cards are included, flagged by `hiddenReason`.
 */
export interface CardGroup {
  cardType: string;
  displayName: string;
  totalInBaseCurrency: number;
  syncStatus: SyncStatus;
  accounts: AccountBalanceItem[];
  hiddenReason?: Nullable<HiddenReason>;
}

/**
 * An institution the user has connected: one Monobank customer, one TrueLayer
 * connection (Revolut, AIB), one Binance account, one IBKR account.
 * Composed by the backend so the frontend never has to guess grouping.
 * Disconnect operates at this level and cascades to every account inside.
 */
export interface Institution {
  institutionId: string;
  provider: string;
  name: string;
  category: AccountCategory;
  totalInBaseCurrency: number;
  syncStatus: SyncStatus;
  lastSyncTimestamp: Nullable<string>;
  lastSuccessfulSyncTimestamp: Nullable<string>;
  accounts: AccountBalanceItem[];
  cards?: Nullable<CardGroup[]>;
  /** Rows the list tucks away and their signed sum; both are already inside `totalInBaseCurrency`. */
  hiddenCount?: number;
  hiddenTotal?: number;
}

export interface CategorySummary {
  category: AccountCategory;
  totalInBaseCurrency: number;
  institutionCount: number;
  institutions: Institution[];
}

/** One inventory section of the accounts page: a category and how its rows are named. */
export interface CategorySection {
  category: AccountCategory;
  title: string;
  /** What one institution of the section is called ("broker"), pluralised by the template. */
  institutionNoun: string;
  /** What the rows inside an institution are called ("position"). */
  rowNoun: string;
  summary: CategorySummary;
}

export interface AppliedFilters {
  category: Nullable<AccountCategory>;
  provider: Nullable<string>;
}

export interface WealthSummaryResponse {
  totalNetWorth: number;
  baseCurrency: string;
  categories: CategorySummary[];
  appliedFilters: AppliedFilters;
  /** `totalNetWorth` as a typed record (finance-framework §3.6); absent from older API builds. */
  headline?: MaybeHeadline;
}

/** A single row in the net-worth-by-category breakdown shown on the accounts hero. */
export interface NetWorthBreakdownRow {
  label: string;
  color: string;
  value: number;
  institutionCount: number;
  /** Share of the positive total; null for a negative category, which has no share. */
  percent: number | null;
}
