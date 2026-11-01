import {type Nullable} from '@lifekit-hq/ui';

/** The rate behind the figure: the bank's EUR buy rate, when the bank published it, and where it came from. */
export interface PaydayTransferRate {
  rateBuy: number;
  publishedAt: string;
  source: string;
}

/**
 * Mirrors the backend's `PaydayTransferDto`. UAH amounts are in hryvnia, EUR amounts in euro. The rate-derived fields are
 * null while the bank's rate is unavailable; `hasCard` is false when no Monobank credit card is connected.
 */
export interface PaydayTransfer {
  hasCard: boolean;
  owedUah: number;
  fixedOutflowUah: number;
  totalUah: number;
  roundingStepEur: number;
  rate: Nullable<PaydayTransferRate>;
  exactEur: Nullable<number>;
  suggestedEur: Nullable<number>;
  cushionEur: Nullable<number>;
  balanceSyncedAt: Nullable<string>;
}

export interface PaydayTransferSettings {
  fixedOutflowUah: number;
  roundingStepEur: number;
}
