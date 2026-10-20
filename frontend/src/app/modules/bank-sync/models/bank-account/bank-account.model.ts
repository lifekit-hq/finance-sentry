import {type Nullable} from '@lifekit-hq/ui';
import {type AccountIdentity} from '@shared/models/account-identity/account-identity.model';
import {type Timestamped} from '@shared/models/timestamped/timestamped.model';

export type SyncStatus = 'pending' | 'syncing' | 'active' | 'failed' | 'reauth_required';

export interface BankAccount extends AccountIdentity, Timestamped {
  ownerName: string;
  currentBalance: number;
  availableBalance: number;
  syncStatus: SyncStatus;
  lastSyncTimestamp: Nullable<string>;
  lastSyncDurationMs: Nullable<number>;
  provider: string;
  /** Set once a sync retired the account (it vanished from the provider's list, or its card was replaced). */
  closedAt?: Nullable<string>;
  closedReason?: Nullable<string>;
  /** The retired account this one replaced, when discovery matched a re-issued card to it. */
  predecessorAccountId?: Nullable<string>;
  /** The last four digits before a card reissue on this same account id; null until one changes. */
  previousLast4?: Nullable<string>;
  /** When discovery saw the last four digits change in place. */
  panChangedAt?: Nullable<string>;
  absentSince?: Nullable<string>;
  /** Monobank card type (`black`, `white`, …) or `card` for a TrueLayer credit card. */
  productType?: Nullable<string>;
  /** A TrueLayer card's `valid_to` as the provider sends it: year-month, e.g. `2027-01`. */
  cardValidTo?: Nullable<string>;
}

export interface AccountsResponse {
  accounts: BankAccount[];
  totalCount: number;
  currency_totals: Record<string, number>;
}

export interface ConnectedMonobankAccount {
  id: string;
  bankName: string;
  accountType: string;
  accountNumberLast4: string;
  ownerName: string;
  currency: string;
  currentBalance: Nullable<number>;
  syncStatus: string;
  provider: string;
}

export interface ConnectMonobankResponse {
  accounts: ConnectedMonobankAccount[];
}
