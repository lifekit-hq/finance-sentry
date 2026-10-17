import {type AccountCategory} from '../../../../shared/models/wealth/wealth.model';

/** localStorage key holding the per-institution open/closed choices this device made on Accounts. */
export const ACCOUNTS_OPEN_STORAGE_KEY = 'fns.accounts.institutionOpen';

/** The earlier key, a list of collapsed banking groups; read once so nobody's choice is lost. */
export const ACCOUNTS_COLLAPSED_STORAGE_KEY = 'fns.accounts.collapsedInstitutions';

/**
 * Whether an institution group is open on a first visit. A bank has a few accounts worth seeing at once; a
 * broker or exchange can hold dozens of positions, so it starts closed with its total in the summary.
 */
export const ACCOUNTS_DEFAULT_OPEN: Readonly<Record<AccountCategory, boolean>> = {
  banking: true,
  other: true,
  brokerage: false,
  crypto: false,
};
