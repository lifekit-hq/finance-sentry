import {type Nullable} from '@lifekit-hq/ui';
export interface WatchlistItem {
  id: string;
  ticker: string;
  exchange: Nullable<string>;
  note: Nullable<string>;
  addedAt: string;
}

export interface AddWatchlistRequest {
  ticker: string;
  exchange: Nullable<string>;
  note: Nullable<string>;
}

/** A row as the page renders it: the item plus the dossier route of its ticker. */
export interface WatchlistRow extends WatchlistItem {
  dossierRoute: string;
}
