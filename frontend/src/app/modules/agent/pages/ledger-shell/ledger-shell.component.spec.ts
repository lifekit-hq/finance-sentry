import {ChangeDetectionStrategy, Component} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {provideRouter} from '@angular/router';
import {RouterTestingHarness} from '@angular/router/testing';

import {LedgerShellComponent} from './ledger-shell.component';

@Component({
  selector: 'fns-stub-page',
  template: '',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
class StubPageComponent {}

describe('LedgerShellComponent tabs', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          {
            path: 'ledger',
            component: LedgerShellComponent,
            children: [
              {path: '', component: StubPageComponent},
              {path: 'decisions', component: StubPageComponent},
            ],
          },
        ]),
      ],
    });
  });

  it('offers Chat and Decisions and marks only the active one', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/ledger/decisions');
    harness.detectChanges();

    const nav = harness.routeNativeElement?.querySelector('nav');
    expect(nav?.getAttribute('aria-label')).toBe('Ledger view');
    const tabs = Array.from<HTMLElement>(nav?.querySelectorAll('a') ?? []);
    expect(tabs.map(tab => tab.textContent?.trim())).toEqual(['Chat', 'Decisions']);
    expect(tabs.map(tab => tab.getAttribute('aria-current'))).toEqual([null, 'page']);
  });

  it('keeps Chat active only on the bare route', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/ledger');
    harness.detectChanges();

    const tabs = Array.from<HTMLElement>(
      harness.routeNativeElement?.querySelectorAll('nav a') ?? []
    );
    expect(tabs.map(tab => tab.getAttribute('aria-current'))).toEqual(['page', null]);
  });
});
