export interface BrokeragePositionDto {
  symbol: string;
  instrumentType: string;
  quantity: number;
  usdValue: number;
  costBasisUsd: Nullable<number>;
  averageCostUsd: Nullable<number>;
}

export interface BrokerageHoldingsDto {
  provider: string;
  syncedAt: Nullable<string>;
  isStale: boolean;
  positions: BrokeragePositionDto[];
  totalUsdValue: number;
}

export interface CryptoHoldingDto {
  asset: string;
  freeQuantity: number;
  lockedQuantity: number;
  usdValue: number;
  /** The venue this row is held on — holdings can span several. */
  provider: string;
  /** Fiat cash held on the venue (e.g. EUR on Revolut X): venue cash, never a crypto position. */
  isFiat: boolean;
}

export interface CryptoHoldingsDto {
  /** The single venue, or 'multiple' / 'none' — read each holding's own provider instead. */
  provider: string;
  syncedAt: Nullable<string>;
  isStale: boolean;
  holdings: CryptoHoldingDto[];
  totalUsdValue: number;
}

export interface Position {
  symbol: string;
  provider: string;
  quantity: number;
  currentValue: number;
  /** Null for venue cash — a currency balance has no unit price. */
  currentPrice: Nullable<number>;
  /** Fiat cash held on a crypto venue — its own asset class, never crypto and never bank cash. */
  isVenueCash: boolean;
  // Provider-supplied P&L only (IBKR cost basis). Null when the provider gives
  // us no cost basis (crypto cost basis is reconstructed on our side, so we do
  // not surface a P&L we can't stand behind).
  pnlPercent: Nullable<number>;
}
