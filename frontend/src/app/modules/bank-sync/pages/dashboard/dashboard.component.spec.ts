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
    isFirstLoad: signal(false),
    isHistoryFirstLoad: signal(false),
    hasCashFlow: signal(false),
    hasProjection: signal(false),
    historyHasHistory: signal(true),
    errorMessage: signal(''),
    hasError: signal(false),
    lastSyncedAt: signal<number | null>(null),
    load: vi.fn(),
    loadNetWorthHistory: vi.fn(),
    historyErrorMessage: signal(null),
    netWorthHeadline: signal(null),
    bookSplitHeadline: signal(null),
    netWorthChangeHeadline: signal(null),
    bookSplitUnavailable: signal(false),
    netWorthChangeUnavailable: signal(false),
    netWorthChangeFormatted: signal(null),
    netWorthChangePercentFormatted: signal(null),
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
      'lk-chip-row[label="History range"]'
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
    const control = el.querySelector('lk-chip-row[label="History range"]');

    control?.dispatchEvent(
      new CustomEvent('lk-chip-row-change', {detail: {selected: ['mtd'], value: 'mtd'}})
    );

    expect(store['setHistoryRange']).toHaveBeenCalledWith('mtd');
  });

  it('puts the chart between the figure and the range control in the hero card', () => {
    const card = render().el.querySelector('cmn-card');
    const order = ['[data-testid="net-worth-value"]', 'cmn-area-chart', 'lk-chip-row'].map(sel =>
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

  it('labels the net-worth figure a change and carries no invested-change line', () => {
    const el = render('3m', undefined, null, {
      netWorthChangeFormatted: signal('+$320'),
      netWorthChangePercentFormatted: signal('+3.2%'),
      netWorthChangeDirection: signal(1),
      scrubDateFormatted: signal(null),
    }).el;
    const change = el.querySelector('[data-testid="net-worth-change"]');

    expect(change?.textContent?.replace(/\s+/g, ' ').trim()).toBe('Change +$320 (+3.2%) · 3M');
    expect(el.querySelector('[data-testid="net-worth-invested-change"]')).toBeNull();
    expect(el.querySelector('[data-testid="net-worth-invested-change-slot"]')).toBeNull();
    expect(el.textContent).not.toContain('Invested change');
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

  describe('quality badges', () => {
    const stale = {
      amount: 100,
      percent: null,
      method: 'VALUE',
      scope: 'umbrella',
      window: {to: '2026-10-15'},
      quality: 'DEGRADED',
      reasons: [{code: 'STALE_SOURCE', source: 'crypto'}],
      currency: 'USD',
    };

    it('shows no badge when the figures are complete or carry no headline', () => {
      const {el} = render();

      expect(el.querySelector('[data-testid="headline-badge"]')).toBeNull();
    });

    it('badges the hero figure Stale and opens the reason from it', () => {
      const {el} = render('3m', undefined, null, {netWorthHeadline: signal(stale)});
      const badge = el.querySelector<HTMLElement>(
        '[data-testid="net-worth-quality"] [data-testid="headline-badge"]'
      );

      expect(badge?.textContent?.trim()).toBe('Stale');
      expect(badge?.getAttribute('aria-label')).toContain('Net worth');
    });

    it('badges the split line and reads N/A when the split is withheld', () => {
      const {el} = render('3m', undefined, null, {
        bookSplitUnavailable: signal(true),
        bookSplitHeadline: signal({
          ...stale,
          amount: null,
          quality: 'UNAVAILABLE',
          reasons: [{code: 'NO_SPLIT'}],
        }),
      });

      expect(el.querySelector('[data-testid="net-worth-split"]')?.textContent).toContain('N/A');
      expect(
        el.querySelector('[data-testid="net-worth-split-quality"] [data-badge="unavailable"]')
      ).not.toBeNull();
    });

    it('reads Change N/A with its badge when the change is withheld', () => {
      const {el} = render('3m', undefined, null, {
        netWorthChangeUnavailable: signal(true),
        netWorthChangeHeadline: signal({
          ...stale,
          amount: null,
          quality: 'UNAVAILABLE',
          reasons: [{code: 'SHORT_HISTORY'}],
        }),
      });

      expect(el.querySelector('[data-testid="net-worth-change"]')?.textContent).toContain(
        'Change N/A'
      );
      expect(el.querySelector('[data-testid="net-worth-change-quality"]')).not.toBeNull();
    });

    it('no longer shows the ad-hoc stale alert under the chart', () => {
      const {el} = render();

      expect(el.textContent).not.toContain('carried forward from the last successful sync');
    });
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
  describe('net-worth chart loading vs empty', () => {
    const CHART = 'cmn-area-chart';
    const SKELETON = '[data-testid="net-worth-chart-skeleton"]';

    it.each([
      ['the page', {isLoading: signal(true), isFirstLoad: signal(true)}],
      ['the history', {isHistoryLoading: signal(true), isHistoryFirstLoad: signal(true)}],
    ])(
      'shows the skeleton, not the chart or its empty state, while %s first loads',
      (_, loading) => {
        const {el} = render('3m', undefined, null, {historyHasHistory: signal(false), ...loading});

        expect(el.querySelector(SKELETON)).not.toBeNull();
        expect(el.querySelector(CHART)).toBeNull();
        // Scoped to the hero card: the category donut below has its own empty copy.
        const hero = el.querySelector('cmn-card')?.textContent;
        expect(hero).not.toContain('No data yet');
        expect(hero).not.toContain('starts after tonight');
      }
    );

    it('says history starts tonight only once loaded with no history', () => {
      const {el} = render('3m', undefined, null, {historyHasHistory: signal(false)});

      expect(el.querySelector(SKELETON)).toBeNull();
      expect(el.querySelector(CHART)).toBeNull();
      expect(el.textContent).toContain("Net worth history starts after tonight's snapshot.");
    });

    it.each([
      ['the page', {isLoading: signal(true)}],
      ['the history', {isHistoryLoading: signal(true)}],
    ])('keeps the chart mounted when %s reloads over existing history', (_, loading) => {
      const {el} = render('3m', undefined, null, {historyHasHistory: signal(true), ...loading});

      expect(el.querySelector(SKELETON)).toBeNull();
      expect(el.querySelector(CHART)).not.toBeNull();
    });

    it('shows the skeleton, not the previous range chart, while this range loads', () => {
      const {el} = render('1y', undefined, null, {
        historyHasHistory: signal(true),
        isHistoryFirstLoad: signal(true),
      });

      expect(el.querySelector(SKELETON)).not.toBeNull();
      expect(el.querySelector(CHART)).toBeNull();
    });

    it('mounts the chart once history has loaded', () => {
      const {el} = render();

      expect(el.querySelector(SKELETON)).toBeNull();
      expect(el.querySelector(CHART)).not.toBeNull();
    });
  });

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

describe('DashboardComponent range-scoped widgets', () => {
  const BAR_CHART = 'cmn-bar-chart';
  const DONUT = 'cmn-donut-chart';
  const TABLE = 'cmn-data-table';
  const SKELETONS = [
    '[data-testid="cash-flow-chart-skeleton"]',
    '[data-testid="category-donut-skeleton"]',
    '[data-testid="category-table-skeleton"]',
  ];
  const data = {
    accountCount: 1,
    topCategories: [{category: 'GROCERIES', totalSpend: 3100, percentOfTotal: 100}],
  };

  it('shows skeletons, not the previous range figures, while this range loads', () => {
    const {el} = render('1y', data, null, {
      hasCashFlow: signal(true),
      isFirstLoad: signal(true),
    });

    for (const selector of SKELETONS) {
      expect(el.querySelector(selector)).not.toBeNull();
    }
    for (const selector of [BAR_CHART, DONUT, TABLE]) {
      expect(el.querySelector(selector)).toBeNull();
    }
    expect(el.textContent).not.toContain('€3,100.00');
  });

  it('keeps the figures while the same range refreshes', () => {
    const {el} = render('1y', data, null, {
      hasCashFlow: signal(true),
      isLoading: signal(true),
    });

    for (const selector of SKELETONS) {
      expect(el.querySelector(selector)).toBeNull();
    }
    for (const selector of [BAR_CHART, DONUT, TABLE]) {
      expect(el.querySelector(selector)).not.toBeNull();
    }
    expect(el.textContent).toContain('€3,100.00');
  });
});
