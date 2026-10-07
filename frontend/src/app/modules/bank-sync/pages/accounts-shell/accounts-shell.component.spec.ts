import {provideHttpClient} from '@angular/common/http';
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

describe('AccountsShellComponent tabs', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideApiBaseUrl('http://localhost'),
        provideRouter([
          {
            path: 'accounts',
            component: AccountsShellComponent,
            children: [
              {path: 'list', component: StubPageComponent},
              {path: 'investments', component: StubPageComponent},
            ],
          },
        ]),
      ],
    });
  });

  it('marks only the active tab with aria-current=page', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/accounts/investments');
    harness.detectChanges();

    const nav = harness.routeNativeElement?.querySelector('nav');
    expect(nav?.getAttribute('aria-label')).toBe('Accounts view');
    const tabs = Array.from<HTMLElement>(nav?.querySelectorAll('a') ?? []);
    expect(tabs.map(tab => tab.textContent?.trim())).toEqual(['Inventory', 'Investments']);
    expect(tabs.map(tab => tab.getAttribute('aria-current'))).toEqual([null, 'page']);
  });
});
