import {provideHttpClient, withXhr} from '@angular/common/http';
import {provideHttpClientTesting} from '@angular/common/http/testing';
import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {By} from '@angular/platform-browser';
import {provideRouter, Router} from '@angular/router';
import {type ChartPointClick, type ChartScrubPoint} from '@lifekit-hq/charts-core';
import {provideApiBaseUrl} from '@lifekit-hq/core';
import {AreaChartComponent, BarChartComponent, DonutChartComponent} from '@lifekit-hq/ui';
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
function fakeStore(
  range: HistoryRange,
  data: unknown = {accountCount: 1, topCategories: []},
  bookSplit: string | null = null,
  overrides: Record<string, unknown> = {}
) {
  const historyRange = signal<HistoryRange>(range);
  const setScrubIndex = vi.fn();
  const setHistoryRange = vi.fn((next: HistoryRange) => historyRange.set(next));
  const real: Record<string, unknown> = {
    historyRange,
    setHistoryRange,
    setScrubIndex,
    data: signal(data),
    baseCurrency: signal('EUR'),
    isLoading: signal(false),
    isHistoryLoading: signal(false),
    hasCashFlow: signal(false),
    hasProjection: signal(false),
    historyHasHistory: signal(true),
    errorMessage: signal(''),
    hasError: signal(false),
    lastSyncedAt: signal<number | null>(null),
    load: vi.fn(),
    loadNetWorthHistory: vi.fn(),
    historyErrorMessage: signal(null),
    netWorthStaleNotice: signal(null),
    netWorthChangeFormatted: signal(null),
    netWorthChangePercentFormatted: signal(null),
    investedChangeFormatted: signal(''),
    investedChangePercentFormatted: signal(''),
    investedChangeDirection: signal(0),
    investedChangeSinceFormatted: signal(null),
    netWorthChartLabel: signal('Net worth by sleeve'),
    bookSplitFormatted: signal(bookSplit),
    ...overrides,
  };
  return new Proxy(real, {
    get: (target, prop: string) => target[prop] ?? signal([]),
  });
}

function render(
  range: HistoryRange = '3m',
  data?: unknown,
  bookSplit: string | null = null,
  overrides: Record<string, unknown> = {}
) {
  const store = fakeStore(range, data, bookSplit, overrides);
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      provideHttpClient(withXhr()),
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
    const control = render().el.querySelector<HTMLElement & {options: {label: string}[]}>(
      'lk-segmented[label="History range"]'
    );

    expect(control?.options.map(o => o.label)).toEqual([
      '1W',
      'MTD',
      '1M',
      '3M',
      'YTD',
      '1Y',
      'ALL',
    ]);
  });

  it('selects a preset through the store when the control changes', () => {
    const {el, store} = render();
    const control = el.querySelector('lk-segmented');

    control?.dispatchEvent(new CustomEvent('lk-segmented-change', {detail: {value: 'mtd'}}));

    expect(store['setHistoryRange']).toHaveBeenCalledWith('mtd');
  });

  it('puts the chart between the figure and the range control in the hero card', () => {
    const card = render().el.querySelector('cmn-card');
    const order = ['[data-testid="net-worth-value"]', 'cmn-area-chart', 'lk-segmented'].map(sel =>
      card?.querySelector(sel)
    );

    expect(order.every(Boolean)).toBe(true);
    expect(order[0]?.compareDocumentPosition(order[1] as Node)).toBe(
      Node.DOCUMENT_POSITION_FOLLOWING
    );
    expect(order[1]?.compareDocumentPosition(order[2] as Node)).toBe(
      Node.DOCUMENT_POSITION_FOLLOWING
    );
  });

  it('states the invested / cash split under the hero figure', () => {
    const el = render('3m', undefined, 'invested €9,300 · cash €700').el;
    const split = el.querySelector('[data-testid="net-worth-split"]');
    const value = el.querySelector('[data-testid="net-worth-value"]');

    expect(split?.textContent?.trim()).toBe('invested €9,300 · cash €700');
    expect(value?.compareDocumentPosition(split as Node)).toBe(Node.DOCUMENT_POSITION_FOLLOWING);
  });

  it('keeps the split slot but leaves it empty when the split is unknown', () => {
    const el = render().el;

    expect(el.querySelector('[data-testid="net-worth-split-slot"]')).not.toBeNull();
    expect(el.querySelector('[data-testid="net-worth-split"]')).toBeNull();
  });

  it('labels the net-worth figure a change, with the invested change on the line below', () => {
    const el = render('3m', undefined, null, {
      netWorthChangeFormatted: signal('+$320'),
      netWorthChangePercentFormatted: signal('+3.2%'),
      netWorthChangeDirection: signal(1),
      scrubDateFormatted: signal(null),
      investedChangeFormatted: signal('+$500'),
      investedChangePercentFormatted: signal('+8.3%'),
      investedChangeDirection: signal(1),
    }).el;
    const change = el.querySelector('[data-testid="net-worth-change"]');
    const invested = el.querySelector('[data-testid="net-worth-invested-change"]');

    expect(change?.textContent?.replace(/\s+/g, ' ').trim()).toBe('Change +$320 (+3.2%) · 3M');
    expect(invested?.textContent?.replace(/\s+/g, ' ').trim()).toBe(
      'Invested change +$500 (+8.3%) · 3M'
    );
    expect(change?.compareDocumentPosition(invested as Node)).toBe(
      Node.DOCUMENT_POSITION_FOLLOWING
    );
  });

  it('dates the invested change from its first split day when that is after the range start', () => {
    const el = render('1y', undefined, null, {
      scrubDateFormatted: signal(null),
      investedChangeFormatted: signal('+$500'),
      investedChangePercentFormatted: signal('+8.3%'),
      investedChangeDirection: signal(1),
      investedChangeSinceFormatted: signal('since Jul 8, 2026'),
    }).el;
    const invested = el.querySelector('[data-testid="net-worth-invested-change"]');

    expect(invested?.textContent?.replace(/\s+/g, ' ').trim()).toBe(
      'Invested change +$500 (+8.3%) · since Jul 8, 2026'
    );
  });

  it('keeps the invested-change slot but leaves it empty below two split days', () => {
    const el = render().el;

    expect(el.querySelector('[data-testid="net-worth-invested-change-slot"]')).not.toBeNull();
    expect(el.querySelector('[data-testid="net-worth-invested-change"]')).toBeNull();
  });

  it('scrubs the hero figure to the point under the pointer and snaps back on release', () => {
    const {fixture, store} = render();
    const chart = fixture.debugElement.query(By.directive(AreaChartComponent))
      .componentInstance as AreaChartComponent;

    chart.scrub.emit({index: 4} as ChartScrubPoint);
    chart.scrubEnd.emit();

    expect(store['setScrubIndex']).toHaveBeenNthCalledWith(1, 4);
    expect(store['setScrubIndex']).toHaveBeenNthCalledWith(2, null);
  });

  it('links the net-worth hero to the accounts list', () => {
    const link = render().el.querySelector<HTMLAnchorElement>('[data-testid="net-worth-link"]');

    expect(link?.getAttribute('href')).toBe('/accounts/list');
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

describe('DashboardComponent chart click-through', () => {
  const pointClick = (seriesLabel: string, index = 0): ChartPointClick => ({
    index,
    seriesIndex: 0,
    label: 'x',
    seriesLabel,
    value: 1,
  });

  beforeEach(() => {
    vi.useFakeTimers({toFake: ['Date']});
    vi.setSystemTime(NOW);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  function chart<T>(fixture: ReturnType<typeof render>['fixture'], type: new (...a: never[]) => T) {
    return fixture.debugElement.query(By.directive(type)).componentInstance as T;
  }

  describe('net-worth area chart', () => {
    it.each([
      ['Banking', '/accounts/list'],
      ['Cash', '/accounts/list'],
      ['Brokerage', '/accounts/investments'],
      ['Brokerage invested', '/accounts/investments'],
      ['Crypto', '/accounts/investments'],
      ['Crypto invested', '/accounts/investments'],
    ])('opens the accounts page behind the %s band', (label, url) => {
      const {fixture} = render();
      const navigateByUrl = vi
        .spyOn(TestBed.inject(Router), 'navigateByUrl')
        .mockResolvedValue(true);

      chart(fixture, AreaChartComponent).pointClick.emit(pointClick(label));

      expect(navigateByUrl).toHaveBeenCalledWith(url);
    });

    it('opens nothing for the unsplit total band', () => {
      const {fixture} = render();
      const navigateByUrl = vi
        .spyOn(TestBed.inject(Router), 'navigateByUrl')
        .mockResolvedValue(true);

      chart(fixture, AreaChartComponent).pointClick.emit(pointClick('No split'));

      expect(navigateByUrl).not.toHaveBeenCalled();
    });
  });

  describe('income vs spending bar chart', () => {
    const overrides = {
      hasCashFlow: signal(true),
      incomeVsSpendingMonthKeys: signal(['2026-06', '2026-07']),
    };

    it('lists the credits of the clicked month for an Income bar', () => {
      const {fixture, navigate} = render('3m', undefined, null, overrides);

      chart(fixture, BarChartComponent).barClick.emit(pointClick('Income', 1));

      expect(navigate).toHaveBeenCalledWith([AppRoute.Transactions], {
        queryParams: {type: 'credit', from: '2026-07-01', to: '2026-07-31'},
      });
    });

    it('lists the debits of the clicked month for a Spending bar', () => {
      const {fixture, navigate} = render('3m', undefined, null, overrides);

      chart(fixture, BarChartComponent).barClick.emit(pointClick('Spending', 0));

      expect(navigate).toHaveBeenCalledWith([AppRoute.Transactions], {
        queryParams: {type: 'debit', from: '2026-06-01', to: '2026-06-30'},
      });
    });

    it('opens nothing for a month the chart no longer holds', () => {
      const {fixture, navigate} = render('3m', undefined, null, overrides);

      chart(fixture, BarChartComponent).barClick.emit(pointClick('Income', 5));

      expect(navigate).not.toHaveBeenCalled();
    });
  });

  describe('category donut', () => {
    const overrides = {categoryChartKeys: signal<(string | null)[]>(['groceries', null])};

    it('lists the clicked category in the selected window', () => {
      const {fixture, navigate} = render('mtd', undefined, null, overrides);

      chart(fixture, DonutChartComponent).segmentClick.emit({
        index: 0,
        label: 'Groceries',
        value: 1,
      });

      expect(navigate).toHaveBeenCalledWith([AppRoute.Transactions], {
        queryParams: {type: 'debit', category: 'groceries', from: '2026-08-01', to: '2026-08-12'},
      });
    });

    it('opens nothing for the folded "Other categories" slice', () => {
      const {fixture, navigate} = render('mtd', undefined, null, overrides);

      chart(fixture, DonutChartComponent).segmentClick.emit({index: 1, label: 'Other', value: 1});

      expect(navigate).not.toHaveBeenCalled();
    });
  });
});

describe('DashboardComponent base currency (#851)', () => {
  it('shows category totals in the base currency the store names, not dollars', () => {
    const {fixture, el} = render('3m', {
      accountCount: 1,
      baseCurrency: 'EUR',
      topCategories: [{category: 'GROCERIES', totalSpend: 3100, percentOfTotal: 100}],
    });
    fixture.detectChanges();

    expect(el.textContent).toContain('€3,100.00');
    expect(el.textContent).not.toContain('$3,100.00');
  });
});

describe('DashboardComponent async states', () => {
  it('shows the connect-account empty state when no account is linked', () => {
    const {el} = render('3m', {accountCount: 0, topCategories: []});

    expect(el.textContent).toContain('Connect your first account');
    expect(el.querySelector('[data-testid="net-worth-link"]')).toBeNull();
  });

  it('shows the content and no empty state when accounts exist', () => {
    const {el} = render();

    expect(el.textContent).not.toContain('Connect your first account');
    expect(el.querySelector('[data-testid="net-worth-link"]')).not.toBeNull();
  });

  it('keeps the dashboard rendered under a persistent error banner', () => {
    const store = fakeStore('3m');
    (store as unknown as {errorMessage: ReturnType<typeof signal<string>>}).errorMessage.set(
      'Failed to load dashboard data.'
    );
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withXhr()),
        provideHttpClientTesting(),
        provideApiBaseUrl('http://localhost/api/v1'),
      ],
    });
    TestBed.overrideComponent(DashboardComponent, {
      set: {providers: [{provide: DashboardStore, useValue: store}]},
    });
    const fixture = TestBed.createComponent(DashboardComponent);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelector('cmn-alert')?.textContent).toContain('Failed to load dashboard data.');
    expect(el.querySelector('[data-testid="net-worth-link"]')).not.toBeNull();
  });

  describe('honest states', () => {
    function renderFailed(data: unknown, offline = false) {
      if (offline) {
        vi.spyOn(window.navigator, 'onLine', 'get').mockReturnValue(false);
      }
      const store = fakeStore('3m', data) as unknown as Record<
        string,
        ReturnType<typeof signal<never>>
      >;
      store['errorMessage'].set('Failed to load dashboard data.' as never);
      store['hasError'].set(true as never);
      TestBed.configureTestingModule({
        providers: [
          provideRouter([]),
          provideHttpClient(withXhr()),
          provideHttpClientTesting(),
          provideApiBaseUrl('http://localhost/api/v1'),
        ],
      });
      TestBed.overrideComponent(DashboardComponent, {
        set: {providers: [{provide: DashboardStore, useValue: store}]},
      });
      const fixture = TestBed.createComponent(DashboardComponent);
      fixture.detectChanges();
      return {store, el: fixture.nativeElement as HTMLElement};
    }

    afterEach(() => {
      vi.restoreAllMocks();
    });

    it('shows an error with Retry, never the connect-account empty state, when nothing loaded', () => {
      const {el, store} = renderFailed(null);

      expect(el.querySelector('cmn-alert')?.textContent).toContain(
        'Failed to load dashboard data.'
      );
      expect(el.textContent).not.toContain('Connect your first account');
      expect(el.querySelector('[data-testid="net-worth-link"]')).toBeNull();
      el.querySelector<HTMLElement>('cmn-alert cmn-button button')?.click();
      expect(store['load']).toHaveBeenCalledOnce();
      expect(store['loadNetWorthHistory']).toHaveBeenCalledOnce();
    });

    it('shows the last data with a last-synced notice instead of an error while offline', () => {
      const {el} = renderFailed({accountCount: 1, topCategories: []}, true);

      expect(el.textContent).not.toContain('Failed to load dashboard data.');
      expect(el.querySelector('[data-testid="offline-notice"]')?.textContent).toContain(
        "You're offline"
      );
      expect(el.querySelector('[data-testid="net-worth-link"]')).not.toBeNull();
    });
  });
});
