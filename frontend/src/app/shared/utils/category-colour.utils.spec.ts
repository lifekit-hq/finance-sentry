import {describe, expect, it} from 'vitest';

import {
  CATEGORY_FALLBACK_STEPS,
  CATEGORY_SERIES_STEP,
} from '../constants/category-colour/category-colour.constants';
import {CategoryColourUtils} from './category-colour.utils';

describe('CategoryColourUtils.of', () => {
  it('gives a mapped category its fixed step', () => {
    expect(CategoryColourUtils.of('FOOD_AND_DRINK')).toEqual({step: 2});
  });

  it('returns the same colour for the same category on every call', () => {
    expect(CategoryColourUtils.of('TRAVEL')).toEqual(CategoryColourUtils.of('TRAVEL'));
    expect(CategoryColourUtils.of('cat-unmapped')).toEqual(CategoryColourUtils.of('cat-unmapped'));
  });

  it('hashes an unmapped key into the asset-clear steps', () => {
    const steps = ['a', 'bb', 'ccc', 'dddd', 'eeeee'].map(
      k => (CategoryColourUtils.of(k) as {step: number}).step
    );
    expect(steps.every(s => CATEGORY_FALLBACK_STEPS.includes(s))).toBe(true);
  });

  it('keeps every mapped category off the Brokerage (1) and Crypto (6) steps', () => {
    expect(Object.values(CATEGORY_SERIES_STEP)).not.toContain(1);
    expect(Object.values(CATEGORY_SERIES_STEP)).not.toContain(6);
  });
});
