export interface WatchlistEntry {
  ticker: string;
}

export interface PaletteHolding {
  symbol: string;
  isVenueCash: boolean;
}

export interface PaletteAccount {
  accountId: string;
  bankName: string;
  accountNumberLast4: string;
}

export interface PaletteEntities {
  holdings: PaletteHolding[];
  watchlist: WatchlistEntry[];
  accounts: PaletteAccount[];
}
