import {describe, expect, it} from 'vitest';

import {UnitPriceUtils} from './unit-price.utils';

describe('UnitPriceUtils', () => {
  describe('perUnit', () => {
    it('divides the total by the quantity', () => {
      expect(UnitPriceUtils.perUnit(1_500, 10)).toBe(150);
    });

    it('is null for a zero quantity', () => {
      expect(UnitPriceUtils.perUnit(1_500, 0)).toBeNull();
    });

    it('is null for a near-zero quantity', () => {
      expect(UnitPriceUtils.perUnit(0.01, 1e-12)).toBeNull();
    });

    it('is null for a non-finite quantity', () => {
      expect(UnitPriceUtils.perUnit(1_500, Number.NaN)).toBeNull();
    });

    it('is null when the total is unknown', () => {
      expect(UnitPriceUtils.perUnit(null, 10)).toBeNull();
      expect(UnitPriceUtils.perUnit(undefined, 10)).toBeNull();
    });

    it('is null for a non-finite total', () => {
      expect(UnitPriceUtils.perUnit(Number.POSITIVE_INFINITY, 10)).toBeNull();
    });
  });

  describe('average', () => {
    it("prefers the provider's own average", () => {
      expect(UnitPriceUtils.average(120, 2_000, 10)).toBe(120);
    });

    it('spreads the cost basis over the units when no average is sent', () => {
      expect(UnitPriceUtils.average(null, 1_200, 10)).toBe(120);
    });

    it('is null when neither the average nor the cost basis is known', () => {
      expect(UnitPriceUtils.average(null, null, 10)).toBeNull();
      expect(UnitPriceUtils.average(undefined, undefined, 10)).toBeNull();
    });

    it('is null for a cost basis over a zero quantity', () => {
      expect(UnitPriceUtils.average(null, 1_200, 0)).toBeNull();
    });
  });

  describe('format', () => {
    it('shows cents for a unit worth one currency unit or more', () => {
      expect(UnitPriceUtils.format(26.4999999)).toBe('$26.50');
    });

    it('keeps more decimals below one currency unit', () => {
      expect(UnitPriceUtils.format(0.0000412)).toBe('$0.0000412');
    });

    it('formats in the given currency', () => {
      expect(UnitPriceUtils.format(120, 'EUR')).toBe('€120.00');
    });

    it('is the em dash for an unknown price', () => {
      expect(UnitPriceUtils.format(null)).toBe('—');
    });
  });
});
