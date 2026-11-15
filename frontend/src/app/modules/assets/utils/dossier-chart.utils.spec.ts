import {describe, expect, it} from 'vitest';

import {type AssetChartDto} from '../models/dossier/dossier.model';
import {type DossierChartView} from '../models/dossier/dossier-view.model';
import {DossierChartUtils} from './dossier-chart.utils';

function chart(overrides: Partial<AssetChartDto> = {}): AssetChartDto {
  return {
    symbol: 'AAPL',
    range: '1Y',
    currency: 'USD',
    points: [
      {date: '2025-10-13', close: 100, benchmark: 100},
      {date: '2025-10-14', close: 110, benchmark: 105},
      {date: '2025-10-15', close: 120, benchmark: 110},
    ],
    benchmarkLabel: 'S&P 500',
    trades: [],
    averageCost: null,
    changePct: 20,
    benchmarkChangePct: 10,
    source: 'yahoo-chart',
    retrievedAt: '2026-10-13T00:00:00Z',
    ...overrides,
  };
}

describe('DossierChartUtils.view', () => {
  it('is null without a chart', () => {
    expect(DossierChartUtils.view(null, '1Y')).toBeNull();
  });

  it('is null when the range has no closes', () => {
    expect(DossierChartUtils.view(chart({points: []}), '1Y')).toBeNull();
  });

  it('draws the price and the benchmark as index-aligned lines', () => {
    const view = DossierChartUtils.view(chart(), '1Y') as DossierChartView;

    expect(view.series.map(s => s.label)).toEqual(['AAPL', 'S&P 500']);
    expect(view.series[0].points.map(p => p.value)).toEqual([100, 110, 120]);
    expect(view.series[0].points[0].time).toBe(Date.parse('2025-10-13'));
    expect(view.series[1].points.map(p => p.value)).toEqual([100, 105, 110]);
    expect(view.averageCostLabel).toBeNull();
    expect(view.caption).toContain('S&P 500');
  });

  it('keeps the benchmark to the days the index closed, whatever the API reports for the whole history', () => {
    const view = DossierChartUtils.view(
      chart({
        points: [
          {date: '1990-01-02', close: 1, benchmark: null},
          {date: '1993-02-01', close: 2, benchmark: 2},
          {date: '2025-10-15', close: 120, benchmark: 110},
        ],
        benchmarkChangePct: null,
      }),
      '1Y'
    ) as DossierChartView;

    expect(view.series.map(s => s.label)).toEqual(['AAPL', 'S&P 500']);
    expect(view.series[1].points).toEqual([
      {time: Date.parse('1993-02-01'), value: 2},
      {time: Date.parse('2025-10-15'), value: 110},
    ]);
  });

  it('leaves the benchmark out when the index has no close in the window', () => {
    const view = DossierChartUtils.view(
      chart({points: [{date: '2025-10-13', close: 100, benchmark: null}]}),
      '1Y'
    ) as DossierChartView;

    expect(view.series.map(s => s.label)).toEqual(['AAPL']);
  });

  it('states the average cost the block cannot draw', () => {
    const view = DossierChartUtils.view(chart({averageCost: 95}), '1Y') as DossierChartView;

    expect(view.series).toHaveLength(2);
    expect(view.averageCostLabel).toBe('Your average cost $95.00');
  });

  it('states no average cost for a zero cost', () => {
    expect(
      (DossierChartUtils.view(chart({averageCost: 0}), '1Y') as DossierChartView).averageCostLabel
    ).toBeNull();
  });
});

describe('DossierChartUtils.view trades', () => {
  it('lists the user fills with their side and a plain-language title', () => {
    const view = DossierChartUtils.view(
      chart({
        trades: [
          {date: '2025-10-14', side: 'buy', quantity: 5, price: 110, currency: 'USD'},
          {date: '2025-10-15', side: 'sell', quantity: 2, price: 120, currency: 'USD'},
        ],
      }),
      '1Y'
    ) as DossierChartView;

    expect(view.trades.map(t => t.side)).toEqual(['buy', 'sell']);
    expect(view.trades[0].title).toMatch(/^Bought 5 at \$110\.00 · /);
    expect(view.trades[1].title).toMatch(/^Sold 2 at \$120\.00 · /);
  });

  it('has no trades when the user never traded it', () => {
    expect((DossierChartUtils.view(chart(), '1Y') as DossierChartView).trades).toEqual([]);
  });
});

describe('DossierChartUtils.view benchmark coverage of the period', () => {
  const longHistory = (): AssetChartDto =>
    chart({
      points: [
        {date: '1990-01-02', close: 1, benchmark: null},
        {date: '1992-06-01', close: 2, benchmark: null},
        {date: '1993-02-01', close: 3, benchmark: 3},
        {date: '2025-09-01', close: 100, benchmark: 90},
        {date: '2025-10-15', close: 120, benchmark: 110},
      ],
      benchmarkChangePct: null,
    });

  it('leaves the index out on a period that opens before the index existed', () => {
    const view = DossierChartUtils.view(longHistory(), 'MAX') as DossierChartView;

    expect(view.series.map(s => s.label)).toEqual(['AAPL']);
    expect(view.caption).not.toContain('S&P 500');
  });

  it('overlays the index on a period it fully covers, whatever the whole history lacks', () => {
    const view = DossierChartUtils.view(longHistory(), '1Y') as DossierChartView;

    expect(view.series.map(s => s.label)).toEqual(['AAPL', 'S&P 500']);
  });

  it('overlays the index when it starts within the tolerance of the first close', () => {
    const view = DossierChartUtils.view(
      chart({
        points: [
          {date: '2025-10-13', close: 100, benchmark: null},
          {date: '2025-10-15', close: 110, benchmark: 105},
          {date: '2025-10-16', close: 120, benchmark: 110},
        ],
      }),
      '1Y'
    ) as DossierChartView;

    expect(view.series.map(s => s.label)).toEqual(['AAPL', 'S&P 500']);
  });

  it('leaves the index out when it starts later than the tolerance after the first close', () => {
    const view = DossierChartUtils.view(
      chart({
        points: [
          {date: '2025-10-01', close: 100, benchmark: null},
          {date: '2025-10-08', close: 110, benchmark: 105},
          {date: '2025-10-15', close: 120, benchmark: 110},
        ],
      }),
      '1Y'
    ) as DossierChartView;

    expect(view.series.map(s => s.label)).toEqual(['AAPL']);
  });
});

describe('DossierChartUtils.caption', () => {
  it('names the index and the dotted line when it overlays', () => {
    expect(DossierChartUtils.caption('AAPL', 'S&P 500')).toContain('against S&P 500');
  });

  it('names the price and the dotted line alone', () => {
    expect(DossierChartUtils.caption('AAPL', false)).toBe(
      'AAPL price (solid line); the dotted line marks the price at the start of the period.'
    );
  });
});
