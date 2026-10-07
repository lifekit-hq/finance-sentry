import {describe, expect, it} from 'vitest';

import {type DossierQuoteDto} from '../models/dossier/dossier.model';
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
