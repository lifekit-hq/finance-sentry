import {provideHttpClient, withXhr} from '@angular/common/http';
import {provideHttpClientTesting} from '@angular/common/http/testing';
import {ChangeDetectionStrategy, Component, signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {provideRouter} from '@angular/router';
import {RouterTestingHarness} from '@angular/router/testing';
import {provideApiBaseUrl} from '@lifekit-hq/core';
import {CmnPullRefreshService} from '@lifekit-hq/ui';

import {AccountsStore} from '../../store/accounts/accounts.store';
import {ConnectStore} from '../../store/connect/connect.store';
import {AccountsShellComponent} from './accounts-shell.component';

@Component({
  selector: 'fns-stub-page',
  template: '',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
class StubPageComponent {}

describe('AccountsShellComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withXhr()),
        provideHttpClientTesting(),
        provideApiBaseUrl('http://localhost'),
        provideRouter([
          {
            path: 'accounts',
            component: AccountsShellComponent,
            children: [{path: 'list', component: StubPageComponent}],
          },
        ]),
      ],
    });
  });

  it('renders the routed page with no tab bar: Accounts is the one inventory', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/accounts/list');
    harness.detectChanges();

    const element = harness.routeNativeElement;
    expect(element?.querySelector('nav')).toBeNull();
    expect(element?.querySelector('fns-stub-page')).not.toBeNull();
  });

  it('registers the accounts reload for pull to refresh', async () => {
    const load = vi.fn();
    TestBed.overrideComponent(AccountsShellComponent, {
      set: {
        providers: [
          {provide: AccountsStore, useValue: {load, isLoading: signal(false)}},
          ConnectStore,
        ],
      },
    });
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/accounts/list');
    harness.detectChanges();
    const pullRefresh = TestBed.inject(CmnPullRefreshService);

    expect(pullRefresh.available()).toBe(true);
    await pullRefresh.refresh();

    expect(load).toHaveBeenCalledTimes(1);
  });
});
