import {describe, expect, it} from 'vitest';

import {
  type DossierPositionSection,
  type DossierUserTargetDto,
  type PositionReturnsDto,
  type ValuationSnapshotDto,
} from '../models/dossier/dossier.model';
import {
  type DossierReturnsView,
  type SizeCapView,
  type TargetBandView,
} from '../models/dossier/dossier-view.model';
import {DossierPositionUtils} from './dossier-position.utils';

const NO_METRIC = {
  value: null,
  fiveYearAvg: null,
  historyWindowYears: null,
  historyUnavailable: true,
};

function valuation(overrides: Partial<ValuationSnapshotDto> = {}): ValuationSnapshotDto {
  return {
    ticker: 'AAPL',
    notApplicable: false,
    price: 100,
    isStale: false,
    metrics: {
      trailingPe: NO_METRIC,
      forwardPe: NO_METRIC,
      evToEbitda: NO_METRIC,
      dividendYield: NO_METRIC,
    },
    consensusTarget: 120,
    impliedUpsidePct: 20,
    peerSet: null,
    sources: [],
    retrievedAt: '2026-10-13T00:00:00Z',
    name: null,
    sector: null,
    industry: null,
    priceAsOf: null,
    targetLow: 90,
    targetHigh: 150,
    analystCount: 38,
    currency: 'USD',
    ...overrides,
  };
}

function returns(overrides: Partial<PositionReturnsDto> = {}): PositionReturnsDto {
  return {
    unrealizedUsd: -3690,
    realizedUsd: 519,
    dividendsUsd: 0,
    currencyUsd: null,
    totalUsd: -3171,
    coverage: 'complete',
    averageCostNative: 100,
    costCurrency: 'USD',
    fillCount: 4,
    ...overrides,
  };
}

function position(overrides: Partial<DossierPositionSection> = {}): DossierPositionSection {
  return {
    provider: 'ibkr',
    assetClass: 'Equities',
    quantity: 10,
    currentValueUsd: 1000,
    costBasisUsd: 900,
    unrealizedPnlUsd: 100,
    unrealizedPnlPercent: 11.1,
    shareOfBookPercent: 12.2,
    taxLots: [],
    positionCapPercent: 10,
    ...overrides,
  };
}

const MINE: DossierUserTargetDto = {
  targetPrice: 130,
  currency: 'EUR',
  updatedAt: '2026-10-13T00:00:00Z',
};

describe('DossierPositionUtils.returns', () => {
  it('is null without a decomposition', () => {
    expect(DossierPositionUtils.returns(undefined)).toBeNull();
    expect(DossierPositionUtils.returns(null)).toBeNull();
  });

  it('scales every leg against the largest and keeps an unknown leg unknown', () => {
    const view = DossierPositionUtils.returns(returns()) as DossierReturnsView;

    expect(view.legs.map(l => [l.key, l.value, l.barPct])).toEqual([
      ['unrealized', -3690, 100],
      ['realized', 519, (519 / 3690) * 100],
      ['dividends', 0, 0],
      ['currency', null, 0],
    ]);
    expect(view.total).toBe(-3171);
    expect(view.note).toBe('From every ingested fill (4 fills).');
  });

  it('names a single fill in the singular', () => {
    expect(
      (DossierPositionUtils.returns(returns({fillCount: 1})) as DossierReturnsView).note
    ).toContain('(1 fill)');
  });

  it('explains partial coverage', () => {
    expect(
      (DossierPositionUtils.returns(returns({coverage: 'partial'})) as DossierReturnsView).note
    ).toContain('only part');
  });

  it('draws no bars when every leg is zero or unknown', () => {
    const view = DossierPositionUtils.returns(
      returns({unrealizedUsd: 0, realizedUsd: null, dividendsUsd: null, coverage: 'unavailable'})
    ) as DossierReturnsView;

    expect(view.legs.every(l => l.barPct === 0)).toBe(true);
  });
});

describe('DossierPositionUtils.sizeCap', () => {
  it('is null without a position', () => {
    expect(DossierPositionUtils.sizeCap(null)).toBeNull();
  });

  it('is null without a share of book', () => {
    expect(DossierPositionUtils.sizeCap(position({shareOfBookPercent: null}))).toBeNull();
  });

  it('is null for a negative share', () => {
    expect(DossierPositionUtils.sizeCap(position({shareOfBookPercent: -1}))).toBeNull();
  });

  it('flags a position over the cap and keeps both inside the bar', () => {
    const view = DossierPositionUtils.sizeCap(position()) as SizeCapView;

    expect(view.overCap).toBe(true);
    expect(view.fillPct).toBeCloseTo(80);
    expect(view.capMarkPct).toBeCloseTo((10 / (12.2 * 1.25)) * 100);
  });

  it('shows no cap mark when the IPS sets none', () => {
    const view = DossierPositionUtils.sizeCap(position({positionCapPercent: null})) as SizeCapView;

    expect(view.capMarkPct).toBeNull();
    expect(view.overCap).toBe(false);
  });

  it('draws an empty bar for a zero share with no cap', () => {
    expect(
      DossierPositionUtils.sizeCap(position({shareOfBookPercent: 0, positionCapPercent: undefined}))
    ).toEqual({sharePct: 0, capPct: null, fillPct: 0, capMarkPct: null, overCap: false});
  });
});

describe('DossierPositionUtils.targetBand', () => {
  it('is null without a price', () => {
    expect(DossierPositionUtils.targetBand(valuation(), MINE, null)).toBeNull();
  });

  it('is null with nothing to compare the price with', () => {
    expect(
      DossierPositionUtils.targetBand(
        valuation({consensusTarget: null, targetLow: null, targetHigh: null}),
        null,
        100
      )
    ).toBeNull();
  });

  it('places the price, the analyst mean and the user target on one padded scale', () => {
    const band = DossierPositionUtils.targetBand(valuation(), MINE, 100) as TargetBandView;

    // Scale 84..156: padding is a tenth of the 90..150 span on either side.
    expect(band.lowPct).toBeCloseTo((6 / 72) * 100);
    expect(band.highPct).toBeCloseTo((66 / 72) * 100);
    expect(band.markers.map(m => m.key)).toEqual(['price', 'analyst', 'mine']);
    expect(band.markers[0].leftPct).toBeCloseTo((16 / 72) * 100);
    expect(band.currency).toBe('USD');
    expect(band.source).toBe('Yahoo Finance · 38 analysts');
  });

  it('leaves the user marker out without a target of their own', () => {
    const band = DossierPositionUtils.targetBand(valuation(), null, 100) as TargetBandView;

    expect(band.markers.map(m => m.key)).toEqual(['price', 'analyst']);
  });

  it('ignores an inverted range and a crypto valuation', () => {
    const inverted = DossierPositionUtils.targetBand(
      valuation({targetLow: 200, targetHigh: 100, consensusTarget: null}),
      MINE,
      100
    ) as TargetBandView;
    expect(inverted.low).toBeNull();
    expect(inverted.source).toBeNull();
    expect(inverted.currency).toBe('USD');

    const crypto = DossierPositionUtils.targetBand(
      valuation({notApplicable: true}),
      MINE,
      100
    ) as TargetBandView;
    expect(crypto.markers.map(m => m.key)).toEqual(['price', 'mine']);
    expect(crypto.currency).toBe('EUR');
  });

  it('falls back to USD and centres equal marks when price and target coincide', () => {
    const band = DossierPositionUtils.targetBand(
      null,
      {...MINE, targetPrice: 100, currency: null},
      100
    ) as TargetBandView;

    expect(band.currency).toBe('USD');
    expect(band.markers.every(m => Math.abs(m.leftPct - 50) < 1e-9)).toBe(true);
  });

  it('centres the marks when the scale has no span', () => {
    const band = DossierPositionUtils.targetBand(
      valuation({consensusTarget: null, targetLow: null, targetHigh: null}),
      {...MINE, targetPrice: Number.MIN_VALUE},
      Number.MIN_VALUE
    );

    expect(band?.markers[0].leftPct).toBe(50);
  });
});

describe('DossierPositionUtils.targetSource', () => {
  it('names the provider alone without a count', () => {
    expect(DossierPositionUtils.targetSource(null)).toBe('Yahoo Finance');
  });

  it('names a single analyst in the singular', () => {
    expect(DossierPositionUtils.targetSource(valuation({analystCount: 1}))).toBe(
      'Yahoo Finance · 1 analyst'
    );
  });

  it('ignores a zero count', () => {
    expect(DossierPositionUtils.targetSource(valuation({analystCount: 0}))).toBe('Yahoo Finance');
  });
});

describe('DossierPositionUtils.parseTarget', () => {
  it('is null for blank text', () => {
    expect(DossierPositionUtils.parseTarget('  ')).toBeNull();
  });

  it('reads a number with thousands separators', () => {
    expect(DossierPositionUtils.parseTarget(' 1,250.5 ')).toBe(1250.5);
  });

  it('rejects text that is not a number', () => {
    expect(DossierPositionUtils.parseTarget('abc')).toBeNull();
  });

  it('rejects a decimal comma instead of reading 12,5 as 125', () => {
    expect(DossierPositionUtils.parseTarget('12,5')).toBeNull();
    expect(DossierPositionUtils.parseTarget('1,25')).toBeNull();
  });

  it('reads a plain or leading-dot decimal', () => {
    expect(DossierPositionUtils.parseTarget('12.5')).toBe(12.5);
    expect(DossierPositionUtils.parseTarget('.5')).toBe(0.5);
  });

  it('rejects zero and negatives', () => {
    expect(DossierPositionUtils.parseTarget('0')).toBeNull();
    expect(DossierPositionUtils.parseTarget('-3')).toBeNull();
  });

  it('rejects a price past the API ceiling', () => {
    expect(DossierPositionUtils.parseTarget('2000000000')).toBeNull();
  });
});
