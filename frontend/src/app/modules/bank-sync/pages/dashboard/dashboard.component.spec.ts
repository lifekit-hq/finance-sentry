import {provideHttpClient} from '@angular/common/http';
import {provideHttpClientTesting} from '@angular/common/http/testing';
import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {provideRouter, Router} from '@angular/router';
import {provideApiBaseUrl} from '@lifekit-hq/core';
import {afterEach, beforeEach, describe, expect, it, vi} from 'vitest';

import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {type HistoryRange} from '../../models/dashboard/dashboard.model';
import {DashboardStore} from '../../store/dashboard/dashboard.store';
import {DashboardComponent} from './dashboard.component';

// Frozen mid-month so the day windows straddle nothing and every bound is checkable by eye.
const NOW = new Date('2026-08-12T10:00:00.000Z');

/**
 * The page reads dozens of store signals; only the ones that matter here are real, every other
 * property is an empty-list signal, which the chart inputs accept and the text bindings render blank.
 */
function fakeStore(range: HistoryRange) {
  const historyRange = signal<HistoryRange>(range);
  const setHistoryRange = vi.fn((next: HistoryRange) => historyRange.set(next));
  const real: Record<string, unknown> = {
    historyRange,
    setHistoryRange,
    data: signal({accountCount: 1, topCategories: []}),
    isLoading: signal(false),
    isHistoryLoading: signal(false),
    hasCashFlow: signal(false),
    hasProjection: signal(false),
    historyHasHistory: signal(true),
    errorMessage: signal(null),
    historyErrorMessage: signal(null),
    netWorthStaleNotice: signal(null),
    netWorthChangeFormatted: signal(null),
    netWorthChangePercentFormatted: signal(null),
  };
  return new Proxy(real, {
    get: (target, prop: string) => target[prop] ?? signal([]),
  });
}

function render(range: HistoryRange = '3m') {
  const store = fakeStore(range);
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      provideHttpClient(),
      provideHttpClientTesting(),
      provideApiBaseUrl('http://localhost/api/v1'),
    ],
  });
  TestBed.overrideComponent(DashboardComponent, {
    set: {providers: [{provide: DashboardStore, useValue: store}]},
  });
  const fixture = TestBed.createComponent(DashboardComponent);
  fixture.detectChanges();
  const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  return {fixture, store, navigate, el: fixture.nativeElement as HTMLElement};
}

describe('DashboardComponent range presets', () => {
  beforeEach(() => {
    vi.useFakeTimers({toFake: ['Date']});
    vi.setSystemTime(NOW);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('offers the IBKR presets in order, with no 6M', () => {
    const chips = render().el.querySelectorAll('[aria-label="History range"] cmn-chip');

    expect(Array.from(chips, c => c.textContent?.trim())).toEqual([
      '1W',
      'MTD',
      '1M',
      '3M',
      'YTD',
      '1Y',
      'ALL',
    ]);
  });

  it('selects a preset through the store when its chip is clicked', () => {
    const {el, store} = render();
    const mtd = Array.from(el.querySelectorAll('[aria-label="History range"] cmn-chip')).find(
      c => c.textContent?.trim() === 'MTD'
    );

    mtd?.querySelector('button')?.click();

    expect(store['setHistoryRange']).toHaveBeenCalledWith('mtd');
  });

  it('carries the day window into the income and spending drill-downs', () => {
    const {fixture, navigate} = render('1w');

    fixture.componentInstance.goToIncome();
    fixture.componentInstance.goToSpending();

    expect(navigate).toHaveBeenNthCalledWith(1, [AppRoute.Transactions], {
      queryParams: {type: 'credit', from: '2026-08-06', to: '2026-08-12'},
    });
    expect(navigate).toHaveBeenNthCalledWith(2, [AppRoute.Transactions], {
      queryParams: {type: 'debit', from: '2026-08-06', to: '2026-08-12'},
    });
  });

  it('carries the month-to-date window into a category drill-down', () => {
    const {fixture, navigate} = render('mtd');

    fixture.componentInstance.onCategoryClick({category: 'groceries'} as never);

    expect(navigate).toHaveBeenCalledWith([AppRoute.Transactions], {
      queryParams: {type: 'debit', category: 'groceries', from: '2026-08-01', to: '2026-08-12'},
    });
  });

  it('carries the window, and the history loaded around it, into the savings breakdown', () => {
    const {fixture, navigate} = render('ytd');

    fixture.componentInstance.goToBreakdown();

    expect(navigate).toHaveBeenCalledWith([AppRoute.FlowBreakdown], {
      queryParams: {from: '2026-01-01', to: '2026-08-12', months: 7},
    });
  });

  it('points the Breakdown link at the same window', () => {
    const {fixture, el} = render('1w');
    fixture.detectChanges();

    const href = el.querySelector('a[aria-label^="View the breakdown"]')?.getAttribute('href');

    expect(href).toBe('/dashboard/breakdown?from=2026-08-06&to=2026-08-12&months=1');
  });

  it('opens the all-time breakdown without a lower bound', () => {
    const {fixture, navigate} = render('all');

    fixture.componentInstance.goToBreakdown();

    expect(navigate).toHaveBeenCalledWith([AppRoute.FlowBreakdown], {
      queryParams: {to: '2026-08-12', months: 120},
    });
  });
});
