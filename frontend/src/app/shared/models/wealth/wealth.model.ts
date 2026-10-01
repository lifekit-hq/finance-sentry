import {type AccountIdentity} from '../account-identity/account-identity.model';

export type AccountCategory = 'banking' | 'crypto' | 'brokerage' | 'other';

export type SyncStatus = 'synced' | 'syncing' | 'pending' | 'stale' | 'failed' | 'reauth_required';

export interface AccountBalanceItem extends AccountIdentity {
  provider: string;
  category: AccountCategory;
  currentBalance: number;
  balanceInBaseCurrency: Nullable<number>;
  syncStatus: SyncStatus;
  lastSyncTimestamp: Nullable<string>;
  productType?: Nullable<string>;
}

/**
 * A physical card within an institution, grouping its per-currency sub-accounts.
 * Populated by the backend only for providers with a card product (Monobank
 * black/white/…); absent otherwise. Zero-balance sub-accounts are already excluded.
 */
export interface CardGroup {
  cardType: string;
  displayName: string;
  totalInBaseCurrency: number;
  syncStatus: SyncStatus;
  accounts: AccountBalanceItem[];
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
}

/** A single row in the net-worth-by-category breakdown shown on the accounts hero. */
export interface NetWorthBreakdownRow {
  label: string;
  color: string;
  value: number;
  institutionCount: number;
  percent: number;
}
