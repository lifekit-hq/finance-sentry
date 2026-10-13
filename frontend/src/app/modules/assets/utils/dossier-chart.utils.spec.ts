import {describe, expect, it} from 'vitest';

import {type AssetChartDto, type AssetChartTradeDto} from '../models/dossier/dossier.model';
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

const BUY: AssetChartTradeDto = {
  date: '2025-10-14',
  side: 'buy',
  quantity: 5,
  price: 110,
  currency: 'USD',
};

describe('DossierChartUtils.view', () => {
  it('is null without a chart', () => {
    expect(DossierChartUtils.view(null)).toBeNull();
  });

  it('is null when the range has no closes', () => {
    expect(DossierChartUtils.view(chart({points: []}))).toBeNull();
  });

  it('draws the price and the benchmark as index-aligned lines', () => {
    const view = DossierChartUtils.view(chart()) as DossierChartView;

    expect(view.series.map(s => s.label)).toEqual(['Price', 'S&P 500']);
    expect(view.series[0].points.map(p => p.value)).toEqual([100, 110, 120]);
    expect(view.series[1].points.map(p => p.value)).toEqual([100, 105, 110]);
    expect(view.averageCostLabel).toBeNull();
    expect(view.ticks).toEqual([]);
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

  it('leaves the benchmark out when the index has no close in the window', () => {
    const view = DossierChartUtils.view(
      chart({points: [{date: '2025-10-13', close: 100, benchmark: null}]})
    ) as DossierChartView;

    expect(view.series.map(s => s.label)).toEqual(['Price']);
  });

  it('holds the average cost flat across the window and states it', () => {
    const view = DossierChartUtils.view(chart({averageCost: 95})) as DossierChartView;

    expect(view.series[2].points.map(p => p.value)).toEqual([95, 95, 95]);
    expect(view.averageCostLabel).toBe('Your average cost $95.00');
  });

  it('draws no average-cost line for a zero cost', () => {
    expect(
      (DossierChartUtils.view(chart({averageCost: 0})) as DossierChartView).series
    ).toHaveLength(2);
  });
});

describe('DossierChartUtils.ticks', () => {
  it('is empty without closes', () => {
    expect(DossierChartUtils.ticks(chart({points: [], trades: [BUY]}))).toEqual([]);
  });

  it('places a fill under the first close on or after its day', () => {
    const [tick] = DossierChartUtils.ticks(chart({trades: [BUY]}));

    expect(tick.leftPct).toBe(50);
    expect(tick.side).toBe('buy');
  });

  it('pins a fill after the last close to the end', () => {
    const [tick] = DossierChartUtils.ticks(
      chart({trades: [{...BUY, date: '2025-12-01', side: 'sell'}]})
    );

    expect(tick.leftPct).toBe(100);
    expect(tick.side).toBe('sell');
  });

  it('centres the fill on a single-close chart', () => {
    const [tick] = DossierChartUtils.ticks(
      chart({points: [{date: '2025-10-14', close: 110, benchmark: null}], trades: [BUY]})
    );

    expect(tick.leftPct).toBe(50);
  });
});

describe('DossierChartUtils.tradeTitle', () => {
  it('describes a buy', () => {
    expect(DossierChartUtils.tradeTitle(BUY)).toMatch(/^Bought 5 at \$110\.00 · .*2025$/);
  });

  it('describes a sell', () => {
    expect(DossierChartUtils.tradeTitle({...BUY, side: 'sell'})).toMatch(/^Sold 5 at/);
  });
});
