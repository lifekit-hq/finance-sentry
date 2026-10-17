import {type MenuItem} from '@lifekit-hq/ui';

import {type PickableProvider} from '../../../../shared/models/provider/provider.model';

export const MENU_ACTION_RECONNECT = 'reconnect';
export const MENU_ACTION_DISCONNECT = 'disconnect';

export const MENU_ACTION_REOPEN = 'reopen';
export const MENU_ACTION_REMOVE = 'remove';

const RECONNECT_ITEM: MenuItem = {id: MENU_ACTION_RECONNECT, label: 'Reconnect', icon: 'RefreshCw'};
const DISCONNECT_ITEM: MenuItem = {
  id: MENU_ACTION_DISCONNECT,
  label: 'Disconnect',
  icon: 'Unplug',
  destructive: true,
};

/**
 * Providers that re-authorise through the connect flow, with the picker slug and modal title each opens;
 * the rest only offer Disconnect. An Inzhur reconnect is the owner pasting a fresh session.
 */
export const RECONNECT_TARGET: Readonly<Record<string, {slug: PickableProvider; title: string}>> = {
  truelayer: {slug: 'truelayer', title: 'Reconnect bank'},
  inzhur: {slug: 'inzhur', title: 'Reconnect Inzhur'},
};

/** Stable arrays, so the menu input does not change identity on every change-detection pass. */
export const DISCONNECT_ONLY_MENU: MenuItem[] = [DISCONNECT_ITEM];
export const RECONNECT_MENU: MenuItem[] = [RECONNECT_ITEM, DISCONNECT_ITEM];

const REMOVE_ITEM: MenuItem = {
  id: MENU_ACTION_REMOVE,
  label: 'Remove',
  icon: 'Trash2',
  destructive: true,
};

/** A closed account's row menu: bring it back, or remove it and its history (asks first). */
export const CLOSED_ACCOUNT_MENU: MenuItem[] = [
  {id: MENU_ACTION_REOPEN, label: 'Reopen', icon: 'RotateCcw'},
  REMOVE_ITEM,
];

/** A card replaced by a live successor can only be removed; reopening it would double-count its balance. */
export const CLOSED_REPLACED_ACCOUNT_MENU: MenuItem[] = [REMOVE_ITEM];
