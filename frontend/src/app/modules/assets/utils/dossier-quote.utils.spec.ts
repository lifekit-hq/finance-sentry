import {type Nullable} from '@lifekit-hq/ui';
import {describe, expect, it} from 'vitest';

import {
  type AssetDossierDto,
  type DossierPositionSection,
  type DossierQuoteDto,
  type ValuationSnapshotDto,
} from '../models/dossier/dossier.model';
import {DossierQuoteUtils} from './dossier-quote.utils';

function quote(overrides: Partial<DossierQuoteDto> = {}): DossierQuoteDto {
  return {
    ticker: 'AAPL',
    price: 189.3,
    previousClose: 187,
    changePct: 1.2299,
    currency: 'USD',
    ...overrides,
  };
}

describe('DossierQuoteUtils.toMajorUnits', () => {
  it.each([
    ['GBp', 'GBP'],
    ['GBX', 'GBP'],
    ['ZAc', 'ZAR'],
    ['ILA', 'ILS'],
  ])('rescales a %s quote to %s', (minor, major) => {
    expect(
      DossierQuoteUtils.toMajorUnits(quote({price: 2650, previousClose: 2600, currency: minor}))
    ).toEqual({
      ticker: 'AAPL',
      price: 26.5,
      previousClose: 26,
      changePct: 1.2299,
      currency: major,
    });
  });

  it('keeps a missing previous close missing', () => {
    expect(
      DossierQuoteUtils.toMajorUnits(quote({price: 2650, previousClose: null, currency: 'GBp'}))
    ).toMatchObject({price: 26.5, previousClose: null, currency: 'GBP'});
  });

  it('leaves a major-currency quote untouched', () => {
    const usd = quote();
    expect(DossierQuoteUtils.toMajorUnits(usd)).toBe(usd);
    const gbp = quote({currency: 'GBP', price: 26.5});
    expect(DossierQuoteUtils.toMajorUnits(gbp)).toBe(gbp);
  });

  it('passes a missing quote through', () => {
    expect(DossierQuoteUtils.toMajorUnits(null)).toBeNull();
  });
});

describe('DossierQuoteUtils.toHeader', () => {
  it('formats price as money and a gain with a plus sign', () => {
    expect(DossierQuoteUtils.toHeader(quote())).toEqual({
      priceText: '$189.30',
      changeText: '+1.23%',
      direction: 'up',
    });
  });

  it('formats a loss with a minus sign', () => {
    expect(DossierQuoteUtils.toHeader(quote({changePct: -0.5}))).toEqual({
      priceText: '$189.30',
      changeText: '-0.50%',
      direction: 'down',
    });
  });

  it('treats a move that rounds to zero as flat, never "-0.00%"', () => {
    expect(DossierQuoteUtils.toHeader(quote({changePct: -0.004}))).toEqual({
      priceText: '$189.30',
      changeText: '0.00%',
      direction: 'flat',
    });
  });

  it('keeps the price when there is no previous close to measure against', () => {
    expect(DossierQuoteUtils.toHeader(quote({changePct: null, previousClose: null}))).toEqual({
      priceText: '$189.30',
      changeText: null,
      direction: 'flat',
    });
  });

  it('uses the quote currency', () => {
    expect(DossierQuoteUtils.toHeader(quote({currency: 'EUR'}))?.priceText).toBe('€189.30');
  });

  it('returns null for a missing quote', () => {
    expect(DossierQuoteUtils.toHeader(null)).toBeNull();
  });

  it('returns null for an unusable price', () => {
    expect(DossierQuoteUtils.toHeader(quote({price: 0}))).toBeNull();
    expect(DossierQuoteUtils.toHeader(quote({price: Number.NaN}))).toBeNull();
  });
});

function dossier(overrides: Partial<AssetDossierDto> = {}): AssetDossierDto {
  return {
    symbol: 'AAPL',
    position: null,
    thesis: null,
    valuation: null,
    analysts: null,
    recentNews: [],
    nextEarnings: null,
    radarSignals: [],
    generatedAt: '2026-09-03T00:00:00Z',
    ...overrides,
  };
}

function position(assetClass: string): DossierPositionSection {
  return {
    provider: 'ibkr',
    assetClass,
    quantity: 1,
    currentValueUsd: 100,
    costBasisUsd: null,
    unrealizedPnlUsd: null,
    unrealizedPnlPercent: null,
    shareOfBookPercent: null,
    taxLots: [],
  };
}

function valuation(notApplicable: boolean, price: Nullable<number> = 189.3): ValuationSnapshotDto {
  const metric = {
    value: null,
    fiveYearAvg: null,
    historyWindowYears: null,
    historyUnavailable: true,
  };
  return {
    ticker: 'AAPL',
    notApplicable,
    price,
    isStale: false,
    metrics: {trailingPe: metric, forwardPe: metric, evToEbitda: metric, dividendYield: metric},
    consensusTarget: null,
    impliedUpsidePct: null,
    peerSet: null,
    sources: [],
    retrievedAt: '2026-09-03T00:00:00Z',
    name: null,
    sector: null,
    industry: null,
    priceAsOf: null,
  };
}

describe('DossierQuoteUtils.isQuotable', () => {
  it('quotes an equity holding', () => {
    expect(DossierQuoteUtils.isQuotable(dossier({position: position('Equities')}))).toBe(true);
  });

  it('never quotes a crypto holding, whatever the valuation says', () => {
    expect(
      DossierQuoteUtils.isQuotable(
        dossier({symbol: 'BTC', position: position('Crypto'), valuation: valuation(false)})
      )
    ).toBe(false);
  });

  it('quotes an unheld symbol the valuation source classed as an equity', () => {
    expect(DossierQuoteUtils.isQuotable(dossier({valuation: valuation(false)}))).toBe(true);
  });

  it('skips an unheld symbol the valuation source classed as non-equity', () => {
    expect(DossierQuoteUtils.isQuotable(dossier({symbol: 'USD', valuation: valuation(true)}))).toBe(
      false
    );
  });

  it('skips an unheld symbol whose valuation lookup came back empty', () => {
    expect(
      DossierQuoteUtils.isQuotable(dossier({symbol: 'ETH', valuation: valuation(false, null)}))
    ).toBe(false);
  });

  it('skips an unheld symbol with no valuation to classify it', () => {
    expect(DossierQuoteUtils.isQuotable(dossier())).toBe(false);
  });
});

describe('DossierQuoteUtils.toChange', () => {
  it('reads the percent as a ratio and the amount as the gap to the previous close', () => {
    const change = DossierQuoteUtils.toChange(quote());

    expect(change?.percent).toBeCloseTo(0.012299);
    expect(change?.amount).toBeCloseTo(2.3);
  });

  it('carries a loss as negative on both figures', () => {
    const change = DossierQuoteUtils.toChange(
      quote({price: 186.5, previousClose: 187.5, changePct: -0.5333})
    );

    expect(change?.percent).toBeCloseTo(-0.005333);
    expect(change?.amount).toBeCloseTo(-1);
  });

  it('derives the amount from the percent when the quote has no previous close', () => {
    const change = DossierQuoteUtils.toChange(
      quote({price: 110, previousClose: null, changePct: 10})
    );

    expect(change?.percent).toBeCloseTo(0.1);
    expect(change?.amount).toBeCloseTo(10);
  });

  it('has no change for a move of -100% with no previous close', () => {
    expect(
      DossierQuoteUtils.toChange(quote({price: 1, previousClose: null, changePct: -100}))
    ).toBeNull();
  });

  it.each([
    ['no percent', quote({changePct: null})],
    ['a non-finite percent', quote({changePct: Number.NaN})],
    ['no price', quote({price: 0})],
    ['a non-finite price', quote({price: Number.NaN})],
  ])('has no change for a quote with %s', (_label, input) => {
    expect(DossierQuoteUtils.toChange(input)).toBeNull();
  });

  it('has no change without a quote', () => {
    expect(DossierQuoteUtils.toChange(null)).toBeNull();
  });
});
