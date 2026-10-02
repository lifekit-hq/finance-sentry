import {type MenuItem} from '@lifekit-hq/ui';

export const MENU_ACTION_RECONNECT = 'reconnect';
export const MENU_ACTION_DISCONNECT = 'disconnect';

const RECONNECT_ITEM: MenuItem = {id: MENU_ACTION_RECONNECT, label: 'Reconnect', icon: 'RefreshCw'};
const DISCONNECT_ITEM: MenuItem = {
  id: MENU_ACTION_DISCONNECT,
  label: 'Disconnect',
  icon: 'Unplug',
  destructive: true,
};

/** Providers that re-authorise through the connect flow; the rest only offer Disconnect. */
export const RECONNECTABLE_PROVIDER = 'truelayer';

/** Stable arrays, so the menu input does not change identity on every change-detection pass. */
export const DISCONNECT_ONLY_MENU: MenuItem[] = [DISCONNECT_ITEM];
export const RECONNECT_MENU: MenuItem[] = [RECONNECT_ITEM, DISCONNECT_ITEM];

/** Status dot colour per sync variant (see `syncStatusVariant`); the relative time sits beside it. */
export const SYNC_DOT_CLASS: Record<'success' | 'warning' | 'error', string> = {
  success: 'bg-status-success',
  warning: 'bg-status-warning',
  error: 'bg-status-error',
};

export const PHONE_MEDIA_QUERY = '(max-width: 639px)';
