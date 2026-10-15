/** Prefix of the client-made id a row carries between an optimistic add and the server's reply. */
export const PENDING_ID_PREFIX = 'pending-';

/** Error code of a delete for an item the server no longer has. */
export const WATCHLIST_ITEM_NOT_FOUND = 'WATCHLIST_ITEM_NOT_FOUND';

export const WATCHLIST_EMPTY_COPY = {
  message: 'Your watchlist is empty',
  subMessage: 'Add a ticker with its buy-the-dip trigger to keep it in view.',
  icon: 'Eye',
} as const;
