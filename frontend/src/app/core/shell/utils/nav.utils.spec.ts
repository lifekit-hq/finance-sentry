import type {NavItem} from '@lifekit-hq/ui';

import {AppRoute} from '../../../shared/enums/app-route/app-route.enum';
import {Permission} from '../../../shared/enums/permission/permission.enum';
import {NAV_ITEMS} from '../app-shell.constants';
import {NavUtils} from './nav.utils';

describe('NavUtils', () => {
  describe('isPermitted', () => {
    it('permits an entry that needs no permission', () => {
      expect(NavUtils.isPermitted(AppRoute.Budgets, [])).toBe(true);
    });

    it('permits a gated entry when the user holds its permission', () => {
      expect(NavUtils.isPermitted(AppRoute.Ledger, [Permission.AiUse])).toBe(true);
    });

    it('hides a gated entry when the user lacks its permission', () => {
      expect(NavUtils.isPermitted(AppRoute.Ledger, [])).toBe(false);
    });
  });

  describe('moreItems', () => {
    it('keeps the nav items past the phone tabs, in nav order', () => {
      expect(NavUtils.moreItems(NAV_ITEMS).map(item => item.label)).toEqual([
        'Budgets',
        'Subscriptions',
        'Events',
        'Ledger',
        'Settings',
      ]);
    });

    it('returns nothing when every item is a tab', () => {
      const tabs: NavItem[] = [{label: 'Home', icon: 'LayoutDashboard', route: AppRoute.Dashboard}];

      expect(NavUtils.moreItems(tabs)).toEqual([]);
    });
  });
});
