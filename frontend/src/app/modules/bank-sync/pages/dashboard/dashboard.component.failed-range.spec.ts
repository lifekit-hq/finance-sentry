import {HttpErrorResponse} from '@angular/common/http';
import {provideHttpClient} from '@angular/common/http';
import {provideHttpClientTesting} from '@angular/common/http/testing';
import {TestBed} from '@angular/core/testing';
import {provideRouter} from '@angular/router';
import {provideApiBaseUrl} from '@lifekit-hq/core';
import {of, Subject} from 'rxjs';
import {afterEach, describe, expect, it, vi} from 'vitest';

import {
  type DashboardData,
  type NetWorthHistoryResponse,
} from '../../models/dashboard/dashboard.model';
import {BankSyncService} from '../../services/bank-sync.service';
import {DashboardComponent} from './dashboard.component';

const OLD_FIGURE = '$111,111';
const SKELETON_TEST_IDS = [
  'net-worth-chart-skeleton',
  'cash-flow-chart-skeleton',
  'category-donut-skeleton',
  'category-table-skeleton',
];

const dashboardData = (netWorth: number): DashboardData =>
  ({
    aggregatedBalance: {USD: netWorth},
    totalNetWorthUsd: netWorth,
    accountCount: 1,
    accountsByType: {},
    monthlyFlow: [],
    topCategories: [],
    baseCurrency: 'USD',
    cashUsd: netWorth,
    investedUsd: 0,
  }) as unknown as DashboardData;

const history = (total: number): NetWorthHistoryResponse => ({
  hasHistory: true,
  snapshots: [
    {
      snapshotDate: '2026-07-31',
      bankingTotal: total,
      brokerageTotal: 0,
      cryptoTotal: 0,
      totalNetWorth: total,
      currency: 'USD',
    },
  ],
});

const serverError = () => new HttpErrorResponse({status: 500, error: {}});

function setup() {
  const dataCalls: Subject<DashboardData>[] = [];
  const historyCalls: Subject<NetWorthHistoryResponse>[] = [];
  const service = {
    getDashboardData: vi.fn(() => {
      const call = new Subject<DashboardData>();
      dataCalls.push(call);
      return call;
    }),
    getNetWorthHistory: vi.fn(() => {
      const call = new Subject<NetWorthHistoryResponse>();
      historyCalls.push(call);
      return call;
    }),
    // The bridge card under the hero has its own store and is not under test here: it settles at once
    // with no bridge to draw, so its skeleton never counts as the page shimmering.
    getNetWorthBridge: vi.fn(() => of({quality: 'UNAVAILABLE', reasons: [], currency: 'USD'})),
  };
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      provideHttpClient(),
      provideHttpClientTesting(),
      provideApiBaseUrl('http://localhost/api/v1'),
      {provide: BankSyncService, useValue: service},
    ],
  });
  const fixture = TestBed.createComponent(DashboardComponent);
  const el = fixture.nativeElement as HTMLElement;
  const detect = (): void => {
    fixture.detectChanges();
    TestBed.tick();
    fixture.detectChanges();
  };
  detect();
  const last = <T>(calls: Subject<T>[]): Subject<T> => calls[calls.length - 1];

  // Loads the 3M range, then asks for 1Y: the old figures are on screen when the request goes out.
  last(dataCalls).next(dashboardData(111_111));
  last(historyCalls).next(history(111_111));
  detect();
  expect(el.textContent).toContain(OLD_FIGURE);
  fixture.componentInstance.store.setHistoryRange('1y');
  detect();

  return {
    el,
    detect,
    store: fixture.componentInstance.store,
    failData: (): void => {
      last(dataCalls).error(serverError());
      detect();
    },
    failHistory: (): void => {
      last(historyCalls).error(serverError());
      detect();
    },
    succeedData: (): void => {
      last(dataCalls).next(dashboardData(222_222));
      detect();
    },
    succeedHistory: (): void => {
      last(historyCalls).next(history(222_222));
      detect();
    },
    retryButton: (): HTMLElement | null =>
      el.querySelector('[data-testid="net-worth-history-error"] cmn-button button'),
    // The Investments card and the twelve-month projection load their own data, outside the range
    // request this spec exercises.
    shimmering: (): number =>
      el.querySelectorAll(
        '.cmn-skeleton-sweep:not(.after\\:hidden):not(fns-investments-card *):not(fns-net-worth-projection *)'
      ).length,
    placeholders: (): number => el.querySelectorAll('.cmn-skeleton-sweep.after\\:hidden').length,
  };
}

describe('DashboardComponent failed range change', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('shimmers while the new range loads', () => {
    const page = setup();

    expect(page.shimmering()).toBeGreaterThan(0);
    expect(page.placeholders()).toBe(0);
    expect(page.el.textContent).not.toContain(OLD_FIGURE);
  });

  it('settles on static placeholders, no old figures and a history Retry when the history fails', () => {
    const page = setup();
    page.succeedData();

    page.failHistory();

    expect(page.shimmering()).toBe(0);
    expect(page.placeholders()).toBeGreaterThan(0);
    expect(
      page.el.querySelector('[data-testid="net-worth-chart-skeleton"] .cmn-skeleton-sweep')
        ?.classList
    ).toContain('after:hidden');
    expect(page.el.textContent).not.toContain(OLD_FIGURE);
    expect(page.retryButton()).not.toBeNull();
  });

  it('renders the new range once the history Retry succeeds', () => {
    const page = setup();
    page.succeedData();
    page.failHistory();

    page.retryButton()?.click();
    page.detect();
    page.succeedHistory();

    expect(page.el.querySelector('[data-testid="net-worth-history-error"]')).toBeNull();
    expect(page.el.querySelector('[data-testid="net-worth-chart-skeleton"]')).toBeNull();
    expect(page.shimmering()).toBe(0);
    expect(page.el.textContent).toContain('$222,222');
    expect(page.el.textContent).not.toContain(OLD_FIGURE);
  });

  it('settles on static placeholders with the error banner and Retry when the data fails', () => {
    const page = setup();
    page.succeedHistory();

    page.failData();

    expect(page.shimmering()).toBe(0);
    for (const testId of SKELETON_TEST_IDS.filter(id => id !== 'net-worth-chart-skeleton')) {
      expect(
        page.el.querySelector(`[data-testid="${testId}"] .cmn-skeleton-sweep`)?.classList
      ).toContain('after:hidden');
    }
    expect(page.el.textContent).toContain('Failed to load dashboard data');
    expect(page.el.textContent).not.toContain(OLD_FIGURE);

    const retry = page.el.querySelector<HTMLElement>('cmn-async-state cmn-button button');
    retry?.click();
    page.detect();
    page.succeedData();

    expect(page.shimmering()).toBe(0);
    expect(page.placeholders()).toBe(0);
    expect(page.el.textContent).toContain('$222,222');
  });
});
