import type {NavItem} from '@lifekit-hq/ui';

import {AppRoute} from '../../shared/enums/app-route/app-route.enum';
import {Permission} from '../../shared/enums/permission/permission.enum';

/** Palette action id that opens the account-connection flow. */
export const CONNECT_ACTION_ID = '_connect';

/**
 * Nav routes and palette entries shown only to users holding the given permission (UX only; the API
 * enforces). Entries not listed here are shown to every signed-in user.
 */
export const PERMISSION_BY_ENTRY: Readonly<Record<string, Permission>> = {
  [AppRoute.Ledger]: Permission.AiUse,
  [AppRoute.SettingsPeople]: Permission.UsersManage,
  [CONNECT_ACTION_ID]: Permission.ConnectionsManage,
};

/**
 * The app's nav destinations, in sidebar order. The shell adds the Alerts badge; the More page lists
 * the ones past the phone tabs.
 */
export const NAV_ITEMS: readonly NavItem[] = [
  {label: 'Home', icon: 'LayoutDashboard', route: AppRoute.Dashboard},
  {label: 'Accounts', icon: 'Building2', route: AppRoute.Accounts},
  {label: 'Transactions', icon: 'ArrowLeftRight', route: AppRoute.Transactions},
  {label: 'Budgets', icon: 'Zap', route: AppRoute.Budgets},
  {label: 'Subscriptions', icon: 'RefreshCw', route: AppRoute.Subscriptions},
  {label: 'Alerts', icon: 'Bell', route: AppRoute.Alerts},
  {label: 'Events', icon: 'CalendarDays', route: AppRoute.Events},
  {label: 'Ledger', icon: 'Sparkles', route: AppRoute.Ledger},
  {label: 'Settings', icon: 'Settings2', route: AppRoute.Settings},
];

/** Nav routes shown as bottom tabs below the md breakpoint; every other nav item is on the More page. */
export const PHONE_TAB_ROUTES: readonly string[] = [
  AppRoute.Dashboard,
  AppRoute.Accounts,
  AppRoute.Transactions,
  AppRoute.Alerts,
];
