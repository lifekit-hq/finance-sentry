export interface WatchlistEntry {
  ticker: string;
}

export interface PaletteHolding {
  symbol: string;
  /** Cash rows (broker or venue) have no dossier, so they are not palette items. */
  assetClass: string;
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
