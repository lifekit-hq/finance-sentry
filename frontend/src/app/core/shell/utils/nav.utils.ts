import type {NavItem} from '@lifekit-hq/ui';

import {PERMISSION_BY_ENTRY, PHONE_TAB_ROUTES} from '../app-shell.constants';

export class NavUtils {
  /** Whether a user holding `permissions` is shown the nav route or palette entry `entry`. */
  public static isPermitted(entry: string, permissions: readonly string[]): boolean {
    const permission = PERMISSION_BY_ENTRY[entry];
    return permission === undefined || permissions.includes(permission);
  }

  /** The nav items past the phone tabs: what the More page lists. */
  public static moreItems(items: readonly NavItem[]): NavItem[] {
    return items.filter(item => !PHONE_TAB_ROUTES.includes(item.route));
  }
}
