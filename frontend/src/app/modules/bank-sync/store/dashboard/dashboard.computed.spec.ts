import {provideHttpClient, withXhr} from '@angular/common/http';
import {provideHttpClientTesting} from '@angular/common/http/testing';
import {TestBed} from '@angular/core/testing';
import {provideApiBaseUrl} from '@lifekit-hq/core';
import {afterEach, beforeEach, describe, expect, it, vi} from 'vitest';

import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {type Headline} from '../../../../shared/models/headline/headline.model';
import {
  type HistoryRange,
  type MonthlyFlow,
  type NetWorthSnapshotDto,
} from '../../models/dashboard/dashboard.model';
import {dashboardComputed} from './dashboard.computed';
import {build, dashboardFor, type Fixture, snapshot} from './dashboard.computed.fixtures';

// Frozen mid-month so "the current month" is genuinely partial.
const NOW = new Date('2026-08-12T00:00:00.000Z');

function flow(month: string, inflow: number, outflow: number): MonthlyFlow {
  return {
    month,
    currency: 'USD',
    inflow,
    outflow,
    net: inflow - outflow,
    inflowUsd: inflow,
    outflowUsd: outflow,
    netUsd: inflow - outflow,
  };
}

function computedFor(monthlyFlow: MonthlyFlow[], range: HistoryRange = '3m') {
  return TestBed.runInInjectionContext(() => dashboardComputed(build({monthlyFlow}, range)));
}

function computedForWindow(
  monthlyFlow: MonthlyFlow[],
  windowFlow: MonthlyFlow[] | undefined,
  range: HistoryRange
) {
  return TestBed.runInInjectionContext(() =>
    dashboardComputed(build({monthlyFlow, windowFlow}, range))
  );
}

/** Three closed months at a steady 4000 in / 2000 out, plus a partial August. */
function steadyHistory(augustInflow: number, augustOutflow: number): MonthlyFlow[] {
  return [
    flow('2026-05', 4000, 2000),
    flow('2026-06', 4000, 2000),
    flow('2026-07', 4000, 2000),
    flow('2026-08', augustInflow, augustOutflow),
  ];
}

describe('dashboardComputed', () => {
  describe('base currency (#851)', () => {
    const euroFixture: Fixture = {
      monthlyFlow: [flow('2026-08', 3000, 3100)],
      totalNetWorthUsd: 1000,
      baseCurrency: 'EUR',
    };

    beforeEach(() => {
      vi.useFakeTimers();
      vi.setSystemTime(NOW);
      TestBed.configureTestingModule({
        providers: [
          provideHttpClient(withXhr()),
          provideHttpClientTesting(),
          provideApiBaseUrl('http://localhost/api/v1'),
        ],
      });
    });

    afterEach(() => {
      vi.useRealTimers();
    });

    it('formats the headline and hero totals in the base currency the API names', () => {
      const c = dashboardFor(euroFixture);

      expect(c.windowSpendingFormatted()).toBe('€3,100.00');
      expect(c.windowInflowFormatted()).toBe('€3,000.00');
      expect(c.totalBalanceFormatted()).toBe('€1,000.00');
      expect(c.baseCurrency()).toBe('EUR');
    });

    it('stays in dollars when the API names no base currency', () => {
      const c = dashboardFor({...euroFixture, baseCurrency: undefined});

      expect(c.windowSpendingFormatted()).toBe('$3,100.00');
      expect(c.baseCurrency()).toBe('USD');
    });
  });

  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(NOW);
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withXhr()),
        provideHttpClientTesting(),
        provideApiBaseUrl('http://localhost/api/v1'),
      ],
    });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  describe('tiles total the selected window', () => {
    it('sums the window months, in-progress month included, and drops older buckets', () => {
      // 3M in August spans June–August; the backend also sends May.
      const c = computedFor(steadyHistory(500, 900));

      expect(c.windowInflowFormatted()).toBe('$8,500.00');
      expect(c.windowSpendingFormatted()).toBe('$4,900.00');
    });

    it('reports just the current month for month to date', () => {
      // The backend always returns the previous month alongside the current one.
      const c = computedFor([flow('2026-07', 4000, 2000), flow('2026-08', 500, 169.42)], 'mtd');

      expect(c.windowInflowFormatted()).toBe('$500.00');
      expect(c.windowSpendingFormatted()).toBe('$169.42');
    });

    it.each(['1w', 'mtd', '1m'] as const)(
      'totals the backend day-window flow, not whole-month buckets, for %s',
      range => {
        // Whole-month history would say 4000 in / 2000 out for July and 500 / 200 for August;
        // the day window is a slice of it and is what the tiles must show.
        const c = computedForWindow(
          [flow('2026-07', 4000, 2000), flow('2026-08', 500, 200)],
          [flow('2026-07', 100, 40), flow('2026-08', 50, 10)],
          range
        );

        expect(c.windowInflowFormatted()).toBe('$150.00');
        expect(c.windowSpendingFormatted()).toBe('$50.00');
      }
    );

    it('falls back to the month buckets for a day window the backend sent no flow for', () => {
      const c = computedForWindow([flow('2026-08', 500, 200)], undefined, 'mtd');

      expect(c.windowInflowFormatted()).toBe('$500.00');
    });

    it('ignores the day-window flow for month-based ranges', () => {
      const c = computedForWindow(steadyHistory(500, 900), [flow('2026-08', 1, 1)], '3m');

      expect(c.windowInflowFormatted()).toBe('$8,500.00');
    });

    it('gates the savings rate on every day window while income has not landed', () => {
      const c = computedForWindow([], [flow('2026-08', 200, 900)], '1w');

      expect(c.windowSavingsRatePercent()).toBeNull();
    });

    it('starts year-to-date at January', () => {
      const c = computedFor(
        [flow('2025-12', 9000, 9000), flow('2026-01', 100, 40), flow('2026-08', 200, 60)],
        'ytd'
      );

      expect(c.windowInflowFormatted()).toBe('$300.00');
      expect(c.windowSpendingFormatted()).toBe('$100.00');
    });

    it('falls back to an em dash when the window has no rows', () => {
      const c = computedFor([]);

      expect(c.windowInflowFormatted()).toBe('—');
      expect(c.windowSpendingFormatted()).toBe('—');
    });
  });

  describe('window savings rate', () => {
    it('withholds the month-to-date rate while income has not landed', () => {
      // $200 against $900 of spending — salary has not posted, so the raw rate would
      // read about -350% and mean nothing.
      const c = computedFor([flow('2026-08', 200, 900)], 'mtd');

      expect(c.windowSavingsRatePercent()).toBeNull();
    });

    it("ignores last month's income when gating the month-to-date rate", () => {
      const c = computedFor([flow('2026-07', 4000, 2000), flow('2026-08', 200, 900)], 'mtd');

      expect(c.windowSavingsRatePercent()).toBeNull();
    });

    it('reports the month-to-date rate once income has substantially landed', () => {
      const c = computedFor([flow('2026-08', 4000, 1000)], 'mtd');

      expect(c.windowSavingsRatePercent()).toBe(75);
    });

    it('reports the rate over a longer window without gating', () => {
      // (4000 + 4000 + 200 - 2000 - 2000 - 900) / 8200 ≈ 40%.
      const c = computedFor(steadyHistory(200, 900));

      expect(c.windowSavingsRatePercent()).toBe(40);
    });

    it('withholds the rate when the window has no income', () => {
      const c = computedFor([flow('2026-08', 0, 300)]);

      expect(c.windowSavingsRatePercent()).toBeNull();
    });
  });

  describe('spending against your normal', () => {
    it('compares the window with the pace of the closed months', () => {
      // May–July spent 6,000 over 92 days; June 1 to August 12 is 73 days at that pace (~4,761).
      const c = computedFor(steadyHistory(500, 900));

      expect(c.windowSpendingVsUsual()).toBe('$139 above your normal');
    });

    it('reads below when the window spent less than usual', () => {
      const c = computedFor([flow('2026-07', 4000, 2000), flow('2026-08', 500, 169.42)], 'mtd');

      expect(c.windowSpendingVsUsual()).toBe('$605 below your normal');
    });

    it('has no comparison before a month has closed', () => {
      expect(computedFor([flow('2026-08', 500, 900)]).windowSpendingVsUsual()).toBeNull();
    });

    it('has no comparison over all time, which has no start', () => {
      expect(computedFor(steadyHistory(500, 900), 'all').windowSpendingVsUsual()).toBeNull();
    });
  });

  describe('where it is', () => {
    const history = [snapshot(1_000, 3_000, 0), snapshot(2_000, 6_000, 2_000)];

    it('lists each band at the latest point with its share and its accounts page', () => {
      const rows = dashboardFor({monthlyFlow: [], netWorthHistory: history}).allocation();

      expect(rows.map(r => [r.label, r.value, r.percent, r.route])).toEqual([
        ['Banking', 2_000, 20, AppRoute.AccountsList],
        ['Brokerage', 6_000, 60, AppRoute.Accounts],
        ['Crypto', 2_000, 20, AppRoute.Accounts],
      ]);
    });

    it('follows the scrubbed point', () => {
      const rows = dashboardFor({
        monthlyFlow: [],
        netWorthHistory: history,
        scrubIndex: 0,
      }).allocation();

      expect(rows.map(r => [r.label, r.value])).toEqual([
        ['Banking', 1_000],
        ['Brokerage', 3_000],
      ]);
    });

    it('keeps a negative band as a row but gives it no donut slice', () => {
      const c = dashboardFor({monthlyFlow: [], netWorthHistory: [snapshot(-500, 1_500, 0)]});

      expect(c.allocation().map(r => r.label)).toEqual(['Banking', 'Brokerage']);
      expect(c.allocationSegments().map(s => [s.label, s.value])).toEqual([['Brokerage', 1_500]]);
    });

    it('is empty without history', () => {
      expect(dashboardFor({monthlyFlow: []}).allocation()).toEqual([]);
    });
  });

  describe('net worth hero', () => {
    function history(first: number, last: number): NetWorthSnapshotDto[] {
      return [snapshot(first, 0, 0), snapshot(last, 0, 0)];
    }

    it('formats the headline symbol-first with cents', () => {
      const c = dashboardFor({monthlyFlow: [], totalNetWorthUsd: 18_981.48});

      expect(c.totalBalanceFormatted()).toBe('$18,981.48');
    });

    it('states the window change in whole dollars and percent', () => {
      const c = dashboardFor({monthlyFlow: [], netWorthHistory: history(10_000, 10_320)});

      expect(c.netWorthChangeFormatted()).toBe('+$320');
      expect(c.netWorthChangePercentFormatted()).toBe('+3.2%');
      expect(c.netWorthChangeDirection()).toBe(1);
    });

    it('signs a decline', () => {
      const c = dashboardFor({monthlyFlow: [], netWorthHistory: history(10_000, 9_500)});

      expect(c.netWorthChangeFormatted()).toBe('-$500');
      expect(c.netWorthChangePercentFormatted()).toBe('-5.0%');
      expect(c.netWorthChangeDirection()).toBe(-1);
    });

    describe('scope change inside the window', () => {
      function dated(first: number, last: number): NetWorthSnapshotDto[] {
        return [
          {...snapshot(first, 0, 0), snapshotDate: '2026-07-01'},
          {...snapshot(last, 0, 0), snapshotDate: '2026-07-31'},
        ];
      }

      it('keeps the amount, drops the percentage and names the linked account', () => {
        const c = dashboardFor({
          monthlyFlow: [],
          netWorthHistory: dated(10_000, 14_000),
          scopeChanges: [{source: 'monobank', kind: 'linked', at: '2026-07-20'}],
        });

        expect(c.netWorthChangeFormatted()).toBe('+$4,000');
        expect(c.netWorthChangePercentFormatted()).toBe('');
        expect(c.netWorthScopeNote()).toBe('Includes newly linked accounts');
      });

      it('names removed accounts, and both when both happened', () => {
        const removed = dashboardFor({
          monthlyFlow: [],
          netWorthHistory: dated(10_000, 6_000),
          scopeChanges: [{source: 'monobank', kind: 'removed', at: '2026-07-10'}],
        });
        const both = dashboardFor({
          monthlyFlow: [],
          netWorthHistory: dated(10_000, 6_000),
          scopeChanges: [
            {source: 'monobank', kind: 'removed', at: '2026-07-10'},
            {source: 'binance', kind: 'linked', at: '2026-07-12'},
          ],
        });

        expect(removed.netWorthScopeNote()).toBe('Includes removed accounts');
        expect(both.netWorthScopeNote()).toBe('Includes newly linked or removed accounts');
      });

      it('keeps the percentage when the link predates the window', () => {
        const c = dashboardFor({
          monthlyFlow: [],
          netWorthHistory: dated(10_000, 10_320),
          scopeChanges: [{source: 'monobank', kind: 'linked', at: '2026-06-15'}],
        });

        expect(c.netWorthChangePercentFormatted()).toBe('+3.2%');
        expect(c.netWorthScopeNote()).toBe('');
      });

      it('ignores a link after the scrubbed day', () => {
        const c = dashboardFor({
          monthlyFlow: [],
          netWorthHistory: dated(10_000, 14_000),
          scrubIndex: 0,
          scopeChanges: [{source: 'monobank', kind: 'linked', at: '2026-07-20'}],
        });

        expect(c.netWorthScopeNote()).toBe('');
      });
    });

    describe('while scrubbing', () => {
      const points = [snapshot(10_000, 0, 0), snapshot(12_000, 0, 0), snapshot(11_000, 0, 0)];

      it('shows the scrubbed point and its change since the window start', () => {
        const c = dashboardFor({
          monthlyFlow: [],
          totalNetWorthUsd: 11_000,
          netWorthHistory: points,
          scrubIndex: 1,
        });

        expect(c.totalBalanceFormatted()).toBe('$12,000.00');
        expect(c.netWorthChangeFormatted()).toBe('+$2,000');
        expect(c.netWorthChangePercentFormatted()).toBe('+20.0%');
        expect(c.scrubDateFormatted()).toBe('31 Jul 2026');
      });

      it('colours the delta by the scrubbed point, not the window end', () => {
        const c = dashboardFor({
          monthlyFlow: [],
          netWorthHistory: [snapshot(10_000, 0, 0), snapshot(8_000, 0, 0), snapshot(11_000, 0, 0)],
          scrubIndex: 1,
        });

        expect(c.netWorthChangeDirection()).toBe(-1);
      });

      it('rests on the live figure when nothing is scrubbed', () => {
        const c = dashboardFor({
          monthlyFlow: [],
          totalNetWorthUsd: 11_000,
          netWorthHistory: points,
        });

        expect(c.totalBalanceFormatted()).toBe('$11,000.00');
        expect(c.netWorthChangeFormatted()).toBe('+$1,000');
        expect(c.scrubDateFormatted()).toBeNull();
      });

      it('reads the charted total on a day a sleeve feed went missing', () => {
        const c = dashboardFor({
          monthlyFlow: [],
          totalNetWorthUsd: 40_000,
          netWorthHistory: [snapshot(40_000, 5_000, 0), snapshot(40_000, 0, 0)],
          scrubIndex: 1,
        });

        expect(c.totalBalanceFormatted()).toBe('$45,000.00');
        expect(c.netWorthChangeFormatted()).toBe('$0');
        expect(c.netWorthChangeDirection()).toBe(0);
      });

      it('keeps a negative sleeve in the scrubbed total and its delta', () => {
        const c = dashboardFor({
          monthlyFlow: [],
          totalNetWorthUsd: 8_000,
          netWorthHistory: [snapshot(1_000, 10_000, 0), snapshot(-2_000, 10_000, 0)],
          scrubIndex: 1,
        });

        expect(c.totalBalanceFormatted()).toBe('$8,000.00');
        expect(c.netWorthChangeFormatted()).toBe('-$3,000');
        expect(c.netWorthChangeDirection()).toBe(-1);
      });

      it('ignores an index past the drawn history', () => {
        const c = dashboardFor({
          monthlyFlow: [],
          totalNetWorthUsd: 11_000,
          netWorthHistory: points,
          scrubIndex: 9,
        });

        expect(c.totalBalanceFormatted()).toBe('$11,000.00');
        expect(c.scrubDateFormatted()).toBeNull();
      });
    });

    it('shows nothing until two snapshots exist', () => {
      const c = dashboardFor({monthlyFlow: [], netWorthHistory: [snapshot(10_000, 0, 0)]});

      expect(c.netWorthChangeFormatted()).toBe('');
      expect(c.netWorthChangePercentFormatted()).toBe('');
      expect(c.netWorthChangeDirection()).toBe(0);
    });
  });
});

describe('dashboardComputed headline quality', () => {
  beforeEach(() => {
    vi.useFakeTimers({toFake: ['Date']});
    vi.setSystemTime(NOW);
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withXhr()),
        provideHttpClientTesting(),
        provideApiBaseUrl('http://localhost/api/v1'),
      ],
    });
  });
  afterEach(() => vi.useRealTimers());

  const headline = (overrides: Partial<Headline> = {}): Headline => ({
    amount: 1000,
    percent: null,
    method: 'VALUE',
    scope: 'umbrella',
    window: {to: '2026-08-12'},
    quality: 'COMPLETE',
    reasons: [],
    currency: 'USD',
    ...overrides,
  });
  const unavailable = (code: string): Headline =>
    headline({amount: null, quality: 'UNAVAILABLE', reasons: [{code}]});
  const compute = (fixture: Fixture) =>
    TestBed.runInInjectionContext(() => dashboardComputed(build(fixture)));

  it('carries the API record on the hero figure unchanged', () => {
    const h = headline({quality: 'ESTIMATED', reasons: [{code: 'FX_TODAY_RATE'}]});
    const c = compute({monthlyFlow: [], totalNetWorthUsd: 1000, netWorthHeadline: h});

    expect(c.netWorthHeadline()).toBe(h);
  });

  it('adds the latest snapshot stale sleeves as STALE_SOURCE when the record omits them', () => {
    const c = compute({
      monthlyFlow: [],
      totalNetWorthUsd: 1000,
      netWorthHeadline: headline(),
      netWorthHistory: [{...snapshot(900, 0, 100), staleSleeves: 'crypto'}],
    });

    expect(c.netWorthHeadline()).toMatchObject({
      quality: 'DEGRADED',
      reasons: [{code: 'STALE_SOURCE', source: 'crypto'}],
    });
  });

  it('takes the stale sources from the latest snapshot per-source rows, one per source', () => {
    const c = compute({
      monthlyFlow: [],
      totalNetWorthUsd: 1000,
      netWorthHeadline: headline(),
      netWorthHistory: [
        {
          ...snapshot(900, 0, 100),
          staleSleeves: 'crypto',
          sources: [
            {sourceId: 'banking', kind: 'Bank', total: 900, health: 'COMPLETE', reasons: []},
            {
              sourceId: 'crypto',
              kind: 'CryptoVenue',
              total: 100,
              health: 'DEGRADED',
              reasons: ['STALE_SOURCE'],
            },
            {
              sourceId: 'inzhur',
              kind: 'Fund',
              total: null,
              health: 'UNAVAILABLE',
              reasons: ['STALE_SOURCE'],
            },
          ],
        },
      ],
    });

    expect(c.netWorthHeadline()).toMatchObject({
      quality: 'DEGRADED',
      reasons: [
        {code: 'STALE_SOURCE', source: 'crypto'},
        {code: 'STALE_SOURCE', source: 'inzhur'},
      ],
    });
  });

  it('builds a degraded record around the amount on an older API build', () => {
    const c = compute({
      monthlyFlow: [],
      totalNetWorthUsd: 1000,
      netWorthHistory: [{...snapshot(900, 0, 100), staleSleeves: 'crypto'}],
    });

    expect(c.netWorthHeadline()).toMatchObject({amount: 1000, quality: 'DEGRADED'});
  });

  it('has no record, and so no badge, when the API sent none and nothing is stale', () => {
    expect(compute({monthlyFlow: [], totalNetWorthUsd: 1000}).netWorthHeadline()).toBeNull();
  });

  it('reads N/A, never 0, for an unavailable hero figure', () => {
    const c = compute({
      monthlyFlow: [],
      totalNetWorthUsd: 0,
      netWorthHeadline: unavailable('NOT_MEASURABLE'),
    });

    expect(c.totalBalanceFormatted()).toBe('N/A');
  });

  it('drops the record, and keeps the scrubbed figure, while scrubbing', () => {
    const c = compute({
      monthlyFlow: [],
      totalNetWorthUsd: 0,
      netWorthHeadline: unavailable('NOT_MEASURABLE'),
      netWorthHistory: [snapshot(100, 0, 0), snapshot(200, 0, 0)],
      scrubIndex: 1,
    });

    expect(c.netWorthHeadline()).toBeNull();
    expect(c.totalBalanceFormatted()).not.toBe('N/A');
  });

  it('marks the change unavailable only when the record says so and there is no change', () => {
    const withheld = compute({monthlyFlow: [], changeHeadline: unavailable('SHORT_HISTORY')});
    const measured = compute({
      monthlyFlow: [],
      changeHeadline: unavailable('SHORT_HISTORY'),
      netWorthHistory: [snapshot(100, 0, 0), {...snapshot(200, 0, 0), snapshotDate: '2026-08-01'}],
    });

    expect(withheld.netWorthChangeUnavailable()).toBe(true);
    expect(measured.netWorthChangeUnavailable()).toBe(false);
  });
});
