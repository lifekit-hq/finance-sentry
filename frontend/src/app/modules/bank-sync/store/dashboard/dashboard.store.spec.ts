import {provideHttpClient} from '@angular/common/http';
import {provideHttpClientTesting} from '@angular/common/http/testing';
import {ChangeDetectionStrategy, Component, inject} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {provideRouter, Router} from '@angular/router';
import {RouterTestingHarness} from '@angular/router/testing';
import {provideApiBaseUrl} from '@lifekit-hq/core';
import {NEVER} from 'rxjs';
import {describe, expect, it, vi} from 'vitest';

import {BankSyncService} from '../../services/bank-sync.service';
import {DashboardStore} from './dashboard.store';

@Component({
  selector: 'fns-dashboard-host',
  template: '',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [DashboardStore],
})
class HostComponent {
  public readonly store = inject(DashboardStore);
}

const service = {
  getDashboardData: vi.fn(() => NEVER),
  getNetWorthHistory: vi.fn(() => NEVER),
};

async function open(url: string) {
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideApiBaseUrl('http://localhost/api/v1'),
      provideRouter([{path: 'dashboard', component: HostComponent}]),
      {provide: BankSyncService, useValue: service},
    ],
  });
  const harness = await RouterTestingHarness.create();
  const host = await harness.navigateByUrl(url, HostComponent);
  await harness.fixture.whenStable();
  return {harness, store: host.store, router: TestBed.inject(Router)};
}

/** Flushes the store's URL-writing effect, then waits for the navigation it started. */
async function settle(fixture: {whenStable: () => Promise<unknown>}): Promise<void> {
  TestBed.tick();
  await fixture.whenStable();
}

describe('DashboardStore range URL sync', () => {
  it('reads a valid range param into state and loads that range', async () => {
    service.getDashboardData.mockClear();
    const {store} = await open('/dashboard?range=ytd');

    expect(store.historyRange()).toBe('ytd');
    expect(service.getDashboardData).toHaveBeenCalledTimes(1);
  });

  it('keeps the 3m default when the range param is missing', async () => {
    const {store, router} = await open('/dashboard');

    expect(store.historyRange()).toBe('3m');
    expect(router.url).toBe('/dashboard');
  });

  it('falls back to the default for an unknown range param', async () => {
    const {store} = await open('/dashboard?range=forever');

    expect(store.historyRange()).toBe('3m');
  });

  it('writes a changed range to the URL without adding a history entry', async () => {
    const {harness, store, router} = await open('/dashboard');
    const entries = history.length;

    store.setHistoryRange('1y');
    await settle(harness.fixture);

    expect(router.url).toBe('/dashboard?range=1y');
    expect(history.length).toBe(entries);
  });

  it('drops the param again when the range returns to the default', async () => {
    const {harness, store, router} = await open('/dashboard?range=1y');

    store.setHistoryRange('3m');
    await settle(harness.fixture);

    expect(router.url).toBe('/dashboard');
  });

  it('follows the URL when it changes after load', async () => {
    const {harness, store} = await open('/dashboard?range=1y');

    await harness.navigateByUrl('/dashboard?range=mtd');

    expect(store.historyRange()).toBe('mtd');
  });
});
