import {provideHttpClient, withXhr} from '@angular/common/http';
import {provideHttpClientTesting} from '@angular/common/http/testing';
import {TestBed} from '@angular/core/testing';
import {provideApiBaseUrl} from '@lifekit-hq/core';
import {beforeEach, describe, expect, it} from 'vitest';

import {ChartColorUtils} from '../../../../shared/utils/chart-color.utils';
import {type NetWorthSnapshotDto} from '../../models/dashboard/dashboard.model';
import {SPLIT_COLOUR} from './dashboard.computed';
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

    it('draws "Other sources" in its own colour, never the "No split" band colour', () => {
      const unsplit = {
        ...withSources(noSplitDay('2026-07-01', 3_000, 5_000, 500), 700),
        totalNetWorth: 9_200,
      };
      const c = projectionFor({
        monthlyFlow: [],
        netWorthHistory: [unsplit, splitDayWithFund('2026-08-01', 4_000, 700)],
      });
      const series = c.netWorthAreaSeries();
      const colour = (label: string) => series.find(s => s.label === label)?.color;

      expect(colour('Other sources')).toBe(ChartColorUtils.canvas(SPLIT_COLOUR.other));
      expect(colour('No split')).toBe(ChartColorUtils.canvas(SPLIT_COLOUR.unsplit));
      expect(colour('Other sources')).not.toBe(colour('No split'));
      expect(new Set(series.map(s => s.color)).size).toBe(series.length);
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
});
