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
    expect(DossierChartUtils.view(null)).toBeNull();
  });

  it('is null when the range has no closes', () => {
    expect(DossierChartUtils.view(chart({points: []}))).toBeNull();
  });

  it('draws the price and the benchmark as index-aligned lines', () => {
    const view = DossierChartUtils.view(chart()) as DossierChartView;

    expect(view.series.map(s => s.label)).toEqual(['AAPL', 'S&P 500']);
    expect(view.series[0].points.map(p => p.value)).toEqual([100, 110, 120]);
    expect(view.series[0].points[0].time).toBe(Date.parse('2025-10-13'));
    expect(view.series[1].points.map(p => p.value)).toEqual([100, 105, 110]);
    expect(view.averageCostLabel).toBeNull();
    expect(view.caption).toContain('S&P 500');
  });

  it('carries the previous benchmark close over a gap and back-fills a leading one', () => {
    const view = DossierChartUtils.view(
      chart({
        points: [
          {date: '2025-10-13', close: 100, benchmark: null},
          {date: '2025-10-14', close: 110, benchmark: 104},
          {date: '2025-10-15', close: 120, benchmark: null},
        ],
      })
    ) as DossierChartView;

    expect(view.series[1].points.map(p => p.value)).toEqual([104, 104, 104]);
  });

  it('leaves the benchmark out when the API reports it does not cover the window', () => {
    const view = DossierChartUtils.view(
      chart({
        points: [
          {date: '2025-10-13', close: 100, benchmark: null},
          {date: '2025-10-20', close: 110, benchmark: 110},
          {date: '2025-10-21', close: 120, benchmark: 115},
        ],
        benchmarkChangePct: null,
      })
    ) as DossierChartView;

    expect(view.series.map(s => s.label)).toEqual(['AAPL']);
  });

  it('leaves the benchmark out when the index has no close in the window', () => {
    const view = DossierChartUtils.view(
      chart({points: [{date: '2025-10-13', close: 100, benchmark: null}]})
    ) as DossierChartView;

    expect(view.series.map(s => s.label)).toEqual(['AAPL']);
  });

  it('states the average cost the block cannot draw', () => {
    const view = DossierChartUtils.view(chart({averageCost: 95})) as DossierChartView;

    expect(view.series).toHaveLength(2);
    expect(view.averageCostLabel).toBe('Your average cost $95.00');
  });

  it('states no average cost for a zero cost', () => {
    expect(
      (DossierChartUtils.view(chart({averageCost: 0})) as DossierChartView).averageCostLabel
    ).toBeNull();
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
