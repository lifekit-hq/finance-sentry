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

/** Nav routes shown as bottom tabs below the md breakpoint; every other nav item sits under "More". */
export const PHONE_TAB_ROUTES: readonly string[] = [
  AppRoute.Dashboard,
  AppRoute.Accounts,
  AppRoute.Transactions,
  AppRoute.Alerts,
];
