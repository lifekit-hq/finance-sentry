import {expect, test} from '@playwright/test';

import {AppRoute} from '../../src/app/shared/enums/app-route/app-route.enum';
import {SCREEN_ROUTES} from './routes';

// `AppRoute` values that are not screens: a redirect, or the parent of a routed child.
const NOT_A_SCREEN: readonly AppRoute[] = [
  AppRoute.Root, // redirects to the dashboard
  AppRoute.Accounts, // the shell around /accounts/list
  AppRoute.Income, // redirects to the transactions ledger
  AppRoute.Investments, // redirects to accounts
];

const pathOf = (route: string): string => route.split('?')[0] ?? route;

test.describe('screen route list', () => {
  test('names every routed screen once', () => {
    const names = SCREEN_ROUTES.map(route => route.name);
    expect(new Set(names).size, 'route names are unique').toBe(names.length);

    const listed = SCREEN_ROUTES.map(route => pathOf(route.path));
    const missing = Object.values(AppRoute)
      .filter(route => !NOT_A_SCREEN.includes(route))
      .filter(route => !listed.some(path => path === route || path.startsWith(`${route}/`)));
    expect(missing, 'AppRoute values with no entry in SCREEN_ROUTES').toEqual([]);
  });
});
