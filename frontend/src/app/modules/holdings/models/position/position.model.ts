import {type Nullable} from '@lifekit-hq/ui';

import {type MaybeHeadline} from '../../../../shared/models/headline/headline.model';
import {type HiddenReason} from '../../../../shared/models/wealth/wealth.model';

export interface BrokeragePositionDto {
  symbol: string;
  instrumentType: string;
  quantity: number;
  usdValue: number;
  costBasisUsd: Nullable<number>;
  averageCostUsd: Nullable<number>;
  /** The broker this position is held with — holdings can span several. */
  provider?: string;
  /** Backend `AssetClassNormalizer` bucket of `instrumentType` (`Cash`, `Bonds`, `RealEstate`, `Equities`, ...). */
  assetClass?: string;
  /** ISO code the broker denominates this position in; `USD` when it only reports USD. */
  currency?: string;
  /** The position's value in `currency` as the broker reported it; null for USD-only rows. */
  nativeValue?: Nullable<number>;
  /** Why the list tucks this position away (display-only: it still counts in the total and weights). */
  hiddenReason?: Nullable<HiddenReason>;
}

export interface BrokerageHoldingsDto {
  provider: string;
  syncedAt: Nullable<string>;
  isStale: boolean;
  positions: BrokeragePositionDto[];
  totalUsdValue: number;
  /** The sleeve total as a typed record (USD); absent from older API builds. */
  headline?: MaybeHeadline;
}

export interface CryptoHoldingDto {
  asset: string;
  freeQuantity: number;
  lockedQuantity: number;
  usdValue: number;
  /** Trade-derived cost of the units held (USD); null when the venue's trade history does not cover them. */
  costBasisUsd?: Nullable<number>;
  /** Trade-derived average purchase price per unit (USD); null alongside `costBasisUsd`. */
  averageBuyPriceUsd?: Nullable<number>;
  /** The venue this row is held on — holdings can span several. */
  provider: string;
  /** Fiat cash held on the venue (e.g. EUR on Revolut X): venue cash, never a crypto position. */
  isFiat: boolean;
  /** Why the list tucks this holding away (display-only: it still counts in the total and weights). */
  hiddenReason?: Nullable<HiddenReason>;
}

export interface CryptoHoldingsDto {
  /** The single venue, or 'multiple' / 'none' — read each holding's own provider instead. */
  provider: string;
  syncedAt: Nullable<string>;
  isStale: boolean;
  holdings: CryptoHoldingDto[];
  totalUsdValue: number;
  /** The sleeve total as a typed record (USD); absent from older API builds. */
  headline?: MaybeHeadline;
}

/** Holdings slice a position belongs to. */
export type PositionAssetClass = 'cash' | 'bonds' | 'realEstate' | 'equity' | 'crypto';

export interface Position {
  symbol: string;
  provider: string;
  /** Brokerage instrument type (`STK`, `CASH`, `BOND`, ...); null for crypto venue rows. */
  instrumentType: Nullable<string>;
  quantity: number;
  currentValue: number;
  /** USD per unit; null for venue cash (a currency balance has no unit price) and for a zero quantity. */
  currentPrice: Nullable<number>;
  /** Average purchase price per unit in USD from the provider's cost; null when no cost is known. */
  averagePrice: Nullable<number>;
  /** Fiat cash held on a crypto venue — its own asset class, never crypto and never bank cash. */
  isVenueCash: boolean;
  /** Slice the row counts in; broker and venue cash are both `cash`. */
  assetClass: PositionAssetClass;
  // Provider-supplied P&L only (IBKR cost basis). Null when the provider gives
  // us no cost basis (crypto cost basis is reconstructed on our side, so we do
  // not surface a P&L we can't stand behind).
  pnlPercent: Nullable<number>;
  /** Unrealized P&L in USD from the same provider cost basis as `pnlPercent`; null when that is. */
  pnlUsd: Nullable<number>;
  /** Currency the provider denominates the row in, when it is not USD (`currentValue` stays USD). */
  nativeCurrency?: string;
  /** `currentValue` in `nativeCurrency`, exactly as the provider reported it (never converted). */
  nativeValue?: Nullable<number>;
  /** The API's verdict that the list tucks this row away; the page groups by it and never re-decides. */
  hiddenReason?: Nullable<HiddenReason>;
}
