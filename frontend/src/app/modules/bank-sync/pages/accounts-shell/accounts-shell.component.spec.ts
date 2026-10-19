import {provideHttpClient, withXhr} from '@angular/common/http';
import {provideHttpClientTesting} from '@angular/common/http/testing';
import {ChangeDetectionStrategy, Component} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {provideRouter} from '@angular/router';
import {RouterTestingHarness} from '@angular/router/testing';
import {provideApiBaseUrl} from '@lifekit-hq/core';

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
});
