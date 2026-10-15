import {provideHttpClient, withXhr} from '@angular/common/http';
import {provideHttpClientTesting} from '@angular/common/http/testing';
import {TestBed} from '@angular/core/testing';
import {provideApiBaseUrl} from '@lifekit-hq/core';
import {beforeEach, describe, expect, it} from 'vitest';

import {type NetWorthSnapshotDto} from '../../models/dashboard/dashboard.model';
import {projectionFor, snapshot} from './dashboard.computed.fixtures';

describe('dashboardComputed cash / invested split', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withXhr()),
        provideHttpClientTesting(),
        provideApiBaseUrl('http://localhost/api/v1'),
      ],
    });
  });

  // A day with a split: the three parts add up to the stored total.
  function splitDay(
    date: string,
    cash: number,
    brokerageInvested: number,
    cryptoInvested: number
  ): NetWorthSnapshotDto {
    return {
      snapshotDate: date,
      bankingTotal: cash,
      brokerageTotal: brokerageInvested,
      cryptoTotal: cryptoInvested,
      totalNetWorth: cash + brokerageInvested + cryptoInvested,
      currency: 'USD',
      cashTotal: cash,
      brokerageInvested,
      cryptoInvested,
    };
  }

  // A split day with a fourth source: its invested value is `otherInvested`, outside the sleeve columns.
  function splitDayWithFund(date: string, cash: number, fundInvested: number): NetWorthSnapshotDto {
    return {
      ...splitDay(date, cash, 5_000, 1_000),
      totalNetWorth: cash + 5_000 + 1_000 + fundInvested,
      otherInvested: fundInvested,
    };
  }

  function noSplitDay(date: string, banking: number, brokerage: number, crypto: number) {
    return {...snapshot(banking, brokerage, crypto), snapshotDate: date};
  }

  const source = (sourceId: string, total: number) => ({
    sourceId,
    kind: 'Bank',
    total,
    health: 'COMPLETE' as const,
    reasons: [],
  });

  const withSources = (day: NetWorthSnapshotDto, fundTotal?: number): NetWorthSnapshotDto => ({
    ...day,
    sources: [
      source('banking', day.bankingTotal),
      source('brokerage', day.brokerageTotal),
      source('crypto', day.cryptoTotal),
      ...(fundTotal === undefined ? [] : [source('fund', fundTotal)]),
    ],
  });

  const values = (series: {points: {value: number}[]}) => series.points.map(p => p.value);

  describe('area chart series', () => {
    it('keeps the sleeve stack when no day has a split', () => {
      const c = projectionFor({
        monthlyFlow: [],
        netWorthHistory: [snapshot(1_000, 2_000, 300), snapshot(1_100, 2_100, 310)],
      });

      expect(c.netWorthAreaSeries().map(s => s.label)).toEqual(['Banking', 'Brokerage', 'Crypto']);
      expect(c.netWorthChartLabel()).toBe('Net worth by sleeve');
    });

    it('adds an "Other sources" band so a fourth source keeps the stack at the total', () => {
      const history = [
        {...withSources(noSplitDay('2026-07-01', 1_000, 2_000, 300), 700), totalNetWorth: 4_000},
        {...withSources(noSplitDay('2026-07-02', 1_100, 2_100, 310), 700), totalNetWorth: 4_210},
      ];
      const c = projectionFor({monthlyFlow: [], netWorthHistory: history});
      const series = c.netWorthAreaSeries();

      expect(series.map(s => s.label)).toEqual(['Banking', 'Brokerage', 'Crypto', 'Other sources']);
      expect(values(series[3])).toEqual([700, 700]);
      history.forEach((day, i) => {
        expect(series.reduce((sum, s) => sum + s.points[i].value, 0)).toBe(day.totalNetWorth);
      });
    });

    it('shows no "Other sources" band when the stored total differs from the sleeves only by cent rounding', () => {
      const rounded = {
        ...withSources(noSplitDay('2026-07-01', 100, 200, 50)),
        totalNetWorth: 350.01,
      };
      const c = projectionFor({monthlyFlow: [], netWorthHistory: [rounded]});

      expect(c.netWorthAreaSeries().map(s => s.label)).toEqual(['Banking', 'Brokerage', 'Crypto']);
    });

    it("stacks a fourth source's invested value as its own band on a split day", () => {
      const history = [
        splitDayWithFund('2026-08-01', 4_000, 700),
        splitDayWithFund('2026-08-02', 4_100, 800),
      ];
      const c = projectionFor({monthlyFlow: [], netWorthHistory: history});
      const series = c.netWorthAreaSeries();

      expect(series.map(s => s.label)).toEqual([
        'Cash',
        'Brokerage invested',
        'Crypto invested',
        'Other sources',
      ]);
      expect(values(series[3])).toEqual([700, 800]);
      history.forEach((day, i) => {
        expect(series.reduce((sum, s) => sum + s.points[i].value, 0)).toBe(day.totalNetWorth);
      });
    });

    it('counts a fourth source on a day with no split among the unsplit band', () => {
      const unsplit = {
        ...withSources(noSplitDay('2026-07-01', 3_000, 5_000, 500), 700),
        totalNetWorth: 9_200,
      };
      const c = projectionFor({
        monthlyFlow: [],
        netWorthHistory: [unsplit, splitDay('2026-08-01', 4_000, 5_000, 1_000)],
      });

      expect(values(c.netWorthAreaSeries()[3])).toEqual([9_200, 0]);
    });

    it('stacks cash, brokerage invested and crypto invested when every day has a split', () => {
      const c = projectionFor({
        monthlyFlow: [],
        netWorthHistory: [
          splitDay('2026-08-01', 4_000, 5_000, 1_000),
          splitDay('2026-08-02', 4_500, 5_200, 900),
        ],
      });
      const series = c.netWorthAreaSeries();

      expect(series.map(s => s.label)).toEqual(['Cash', 'Brokerage invested', 'Crypto invested']);
      expect(values(series[0])).toEqual([4_000, 4_500]);
      expect(values(series[1])).toEqual([5_000, 5_200]);
      expect(values(series[2])).toEqual([1_000, 900]);
      expect(c.netWorthChartLabel()).toBe('Net worth by cash and invested');
    });

    it('stacks to the stored total on every split day, a negative cash band included', () => {
      const history = [
        splitDay('2026-08-01', -500, 8_000, 2_000),
        splitDay('2026-08-02', 300, 8_100, 2_000),
      ];
      const series = projectionFor({
        monthlyFlow: [],
        netWorthHistory: history,
      }).netWorthAreaSeries();

      history.forEach((day, i) => {
        expect(series.reduce((sum, s) => sum + s.points[i].value, 0)).toBe(day.totalNetWorth);
      });
    });

    it('draws a day with no split as one "No split" band, keeping the stack top at the total', () => {
      const c = projectionFor({
        monthlyFlow: [],
        netWorthHistory: [
          noSplitDay('2026-07-01', 3_000, 5_000, 500),
          splitDay('2026-08-01', 4_000, 5_000, 1_000),
        ],
      });
      const series = c.netWorthAreaSeries();

      expect(series.map(s => s.label)).toEqual([
        'Cash',
        'Brokerage invested',
        'Crypto invested',
        'No split',
      ]);
      // Neither dropped nor attributed to a class: the three class bands are empty that day.
      expect(series.slice(0, 3).map(s => s.points[0].value)).toEqual([0, 0, 0]);
      expect(values(series[3])).toEqual([8_500, 0]);
      expect(series.map(s => s.points.length)).toEqual([2, 2, 2, 2]);
      expect(series.reduce((sum, s) => sum + s.points[0].value, 0)).toBe(8_500);
      expect(series.reduce((sum, s) => sum + s.points[1].value, 0)).toBe(10_000);
    });

    it('treats a partly null split as no split', () => {
      const partial = {...splitDay('2026-07-01', 4_000, 5_000, 1_000), cryptoInvested: null};
      const c = projectionFor({
        monthlyFlow: [],
        netWorthHistory: [partial, splitDay('2026-08-01', 4_000, 5_000, 1_000)],
      });

      expect(values(c.netWorthAreaSeries()[3])).toEqual([10_000, 0]);
    });

    it('still carries a missing sleeve forward on the no-split span', () => {
      const c = projectionFor({
        monthlyFlow: [],
        netWorthHistory: [
          noSplitDay('2026-07-01', 3_000, 5_000, 500),
          noSplitDay('2026-07-02', 3_000, 0, 500),
          splitDay('2026-08-01', 4_000, 5_000, 1_000),
        ],
      });

      expect(values(c.netWorthAreaSeries()[3])).toEqual([8_500, 8_500, 0]);
    });
  });

  describe('invested change', () => {
    it('is the change in brokerage + crypto positions, cash excluded', () => {
      const c = projectionFor({
        monthlyFlow: [],
        netWorthHistory: [
          splitDay('2026-08-01', 4_000, 5_000, 1_000),
          splitDay('2026-08-02', 9_000, 5_400, 1_100),
        ],
      });

      // Net worth rose 5,500 mostly on cash; the invested book rose 6,000 -> 6,500.
      expect(c.netWorthChangeFormatted()).toBe('+$5,500');
      expect(c.investedChangeFormatted()).toBe('+$500');
      expect(c.investedChangePercentFormatted()).toBe('+8.3%');
      expect(c.investedChangeDirection()).toBe(1);
    });

    it("includes a fourth source's invested value in the invested change", () => {
      const c = projectionFor({
        monthlyFlow: [],
        netWorthHistory: [
          splitDayWithFund('2026-08-01', 4_000, 700),
          splitDayWithFund('2026-08-02', 4_000, 1_200),
        ],
      });

      expect(c.investedChangeFormatted()).toBe('+$500');
    });

    it('signs a decline', () => {
      const c = projectionFor({
        monthlyFlow: [],
        netWorthHistory: [
          splitDay('2026-08-01', 4_000, 5_000, 1_000),
          splitDay('2026-08-02', 4_000, 4_500, 1_000),
        ],
      });

      expect(c.investedChangeFormatted()).toBe('-$500');
      expect(c.investedChangePercentFormatted()).toBe('-8.3%');
      expect(c.investedChangeDirection()).toBe(-1);
    });

    it('runs only over the days that have a split', () => {
      const c = projectionFor({
        monthlyFlow: [],
        netWorthHistory: [
          noSplitDay('2026-07-01', 100, 100, 100),
          splitDay('2026-08-01', 4_000, 5_000, 1_000),
          splitDay('2026-08-02', 4_000, 5_500, 1_000),
          noSplitDay('2026-08-03', 100, 100, 100),
        ],
      });

      expect(c.investedChangeFormatted()).toBe('+$500');
    });

    it('is hidden when the range has fewer than two days with a split', () => {
      const none = projectionFor({
        monthlyFlow: [],
        netWorthHistory: [snapshot(1_000, 2_000, 300), snapshot(1_100, 2_100, 310)],
      });
      const one = projectionFor({
        monthlyFlow: [],
        netWorthHistory: [
          noSplitDay('2026-07-01', 100, 100, 100),
          splitDay('2026-08-01', 4_000, 5_000, 1_000),
        ],
      });

      for (const c of [none, one]) {
        expect(c.investedChangeFormatted()).toBe('');
        expect(c.investedChangePercentFormatted()).toBe('');
        expect(c.investedChangeDirection()).toBe(0);
      }
    });

    it('states the amount without a percent when the invested book started empty', () => {
      const c = projectionFor({
        monthlyFlow: [],
        netWorthHistory: [
          splitDay('2026-08-01', 4_000, 0, 0),
          splitDay('2026-08-02', 3_000, 1_000, 0),
        ],
      });

      expect(c.investedChangeFormatted()).toBe('+$1,000');
      expect(c.investedChangePercentFormatted()).toBe('');
    });

    it('names the first split day when it is after the start of the window', () => {
      const c = projectionFor({
        monthlyFlow: [],
        netWorthHistory: [
          noSplitDay('2026-07-01', 100, 100, 100),
          splitDay('2026-07-08', 4_000, 5_000, 1_000),
          splitDay('2026-07-09', 4_000, 5_500, 1_000),
        ],
      });

      expect(c.investedChangeSinceFormatted()).toBe('since 8 Jul 2026');
    });

    it('leaves the window to the range label when the split covers all of it', () => {
      const c = projectionFor({
        monthlyFlow: [],
        netWorthHistory: [
          splitDay('2026-07-08', 4_000, 5_000, 1_000),
          splitDay('2026-07-09', 4_000, 5_500, 1_000),
        ],
      });

      expect(c.investedChangeSinceFormatted()).toBeNull();
    });

    describe('while scrubbing', () => {
      const days = [
        noSplitDay('2026-07-01', 100, 100, 100),
        splitDay('2026-08-01', 4_000, 5_000, 1_000),
        splitDay('2026-08-02', 4_000, 5_600, 1_000),
        splitDay('2026-08-03', 4_000, 5_000, 1_500),
      ];

      it('runs from the first split day to the scrubbed day', () => {
        const c = projectionFor({monthlyFlow: [], netWorthHistory: days, scrubIndex: 2});

        expect(c.investedChangeFormatted()).toBe('+$600');
      });

      it('is hidden on a day with no split and on the first split day', () => {
        for (const scrubIndex of [0, 1]) {
          const c = projectionFor({monthlyFlow: [], netWorthHistory: days, scrubIndex});

          expect(c.investedChangeFormatted()).toBe('');
        }
      });

      it('ignores an index past the drawn history', () => {
        const c = projectionFor({monthlyFlow: [], netWorthHistory: days, scrubIndex: 9});

        expect(c.investedChangeFormatted()).toBe('');
      });
    });
  });
});
