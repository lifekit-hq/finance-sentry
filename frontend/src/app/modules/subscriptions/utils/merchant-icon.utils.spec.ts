import {describe, expect, it} from 'vitest';

import {environment} from '../../../../environments/environment';
import {MERCHANT_CATEGORY_ICONS} from '../constants/subscription/merchant-icon.constants';
import {MerchantIconUtils} from './merchant-icon.utils';

describe('MerchantIconUtils.url', () => {
  it('points at our own API for a matched merchant', () => {
    expect(MerchantIconUtils.url(7)).toBe(`${environment.apiBaseUrl}/subscriptions/icons/7`);
  });

  it('serves id 0 like any other id', () => {
    expect(MerchantIconUtils.url(0)).toBe(`${environment.apiBaseUrl}/subscriptions/icons/0`);
  });

  it('is null when no curated merchant matched', () => {
    expect(MerchantIconUtils.url(null)).toBeNull();
    expect(MerchantIconUtils.url(undefined)).toBeNull();
  });
});

describe('MerchantIconUtils.categoryIcon', () => {
  it('maps a category group to its icon', () => {
    expect(MerchantIconUtils.categoryIcon('ENTERTAINMENT')).toBe('Clapperboard');
    expect(MerchantIconUtils.categoryIcon('TRAVEL')).toBe('Plane');
  });

  it('is null for an unmapped, missing or empty category', () => {
    expect(MerchantIconUtils.categoryIcon('UNCATEGORIZED')).toBeNull();
    expect(MerchantIconUtils.categoryIcon('NOT_A_CATEGORY')).toBeNull();
    expect(MerchantIconUtils.categoryIcon('')).toBeNull();
    expect(MerchantIconUtils.categoryIcon(null)).toBeNull();
    expect(MerchantIconUtils.categoryIcon(undefined)).toBeNull();
  });

  it('does not read inherited object keys as categories', () => {
    expect(MerchantIconUtils.categoryIcon('constructor')).toBeNull();
    expect(Object.keys(MERCHANT_CATEGORY_ICONS)).not.toContain('UNCATEGORIZED');
  });
});
