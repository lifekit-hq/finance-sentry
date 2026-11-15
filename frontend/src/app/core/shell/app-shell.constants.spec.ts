import {TestBed} from '@angular/core/testing';
import {provideRouter, Router} from '@angular/router';
import {describe, expect, it} from 'vitest';

import {BANK_SYNC_ROUTES} from '../../modules/bank-sync/bank-sync.routes';
import {AppRoute} from '../../shared/enums/app-route/app-route.enum';
import {NAV_ITEMS, PHONE_TAB_ROUTES} from './app-shell.constants';

// cmn-page enables pull to refresh only when the URL after redirects equals a nav route, so a tab
// whose route redirects elsewhere (/accounts -> /accounts/list) would never respond to a pull.
describe('tab root nav routes', () => {
  it('names the URL the Accounts tab lands on after redirects', async () => {
    TestBed.configureTestingModule({
      providers: [provideRouter([{path: AppRoute.Accounts.slice(1), children: BANK_SYNC_ROUTES}])],
    });
    const router = TestBed.inject(Router);
    await router.navigateByUrl(AppRoute.Accounts);

    const landing = router.url.split(/[?#]/, 1)[0];
    expect(NAV_ITEMS.map(item => item.route)).toContain(landing);
    expect(PHONE_TAB_ROUTES).toContain(landing);
  });
});
