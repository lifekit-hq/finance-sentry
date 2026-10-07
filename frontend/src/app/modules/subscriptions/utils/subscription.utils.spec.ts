import {describe, expect, it} from 'vitest';

import {MERCHANT_SERIES_STEPS} from '../constants/subscription/subscription.constants';
import {SubscriptionUtils} from './subscription.utils';

describe('SubscriptionUtils.getMerchantColor', () => {
  it('returns a chart-series token for a normal name', () => {
    const color = SubscriptionUtils.getMerchantColor('Netflix');
    expect(color).toMatch(/^var\(--color-chart-series-\d\)$/);
  });

  it('keys only onto the steps white initials read on', () => {
    const allowed = MERCHANT_SERIES_STEPS.map(step => `var(--color-chart-series-${step})`);
    for (const name of ['Netflix', 'Spotify', 'Gym', 'iCloud', 'Disney+', 'Vodafone', 'Bolt']) {
      expect(allowed).toContain(SubscriptionUtils.getMerchantColor(name));
    }
  });

  it('returns the same color for the same name (deterministic)', () => {
    expect(SubscriptionUtils.getMerchantColor('Spotify')).toBe(
      SubscriptionUtils.getMerchantColor('Spotify')
    );
  });

  it('returns different colors for different names', () => {
    expect(SubscriptionUtils.getMerchantColor('Netflix')).not.toBe(
      SubscriptionUtils.getMerchantColor('Gym')
    );
  });

  it('returns fallback color for empty string', () => {
    expect(SubscriptionUtils.getMerchantColor('')).toBe('var(--color-chart-series-9)');
  });
});

describe('SubscriptionUtils.daysUntil', () => {
  const now = new Date('2026-10-01T12:00:00Z').getTime();

  it('rounds a partial day up', () => {
    expect(SubscriptionUtils.daysUntil('2026-10-04T00:00:00Z', now)).toBe(3);
  });

  it('is zero on the same instant', () => {
    expect(SubscriptionUtils.daysUntil('2026-10-01T12:00:00Z', now)).toBe(0);
  });

  it('is negative for a past date', () => {
    expect(SubscriptionUtils.daysUntil('2026-09-29T12:00:00Z', now)).toBe(-2);
  });
});

describe('SubscriptionUtils.installmentProgress', () => {
  it('shows paid, term and remaining for a fixed-term plan', () => {
    expect(
      SubscriptionUtils.installmentProgress({
        occurrenceCount: 3,
        termCount: 12,
        remainingPayments: 9,
      })
    ).toBe('3 / 12 paid · 9 left');
  });

  it('omits the remainder when it is unknown', () => {
    expect(
      SubscriptionUtils.installmentProgress({
        occurrenceCount: 3,
        termCount: 12,
        remainingPayments: null,
      })
    ).toBe('3 / 12 paid');
  });

  it('counts payments when there is no term', () => {
    expect(
      SubscriptionUtils.installmentProgress({
        occurrenceCount: 1,
        termCount: null,
        remainingPayments: null,
      })
    ).toBe('1 payment');
    expect(
      SubscriptionUtils.installmentProgress({
        occurrenceCount: 4,
        termCount: null,
        remainingPayments: null,
      })
    ).toBe('4 payments');
  });
});

describe('SubscriptionUtils.chargesQuery', () => {
  it('searches a detected row by its statement name', () => {
    expect(SubscriptionUtils.chargesQuery({isManual: false, merchantName: ' Netflix '})).toBe(
      'Netflix'
    );
  });

  it('returns null for a hand-typed label, a composed top-up name, or a blank name', () => {
    expect(SubscriptionUtils.chargesQuery({isManual: true, merchantName: 'Car loan'})).toBeNull();
    expect(
      SubscriptionUtils.chargesQuery({isManual: false, merchantName: 'Mobile top-up 0057'})
    ).toBeNull();
    expect(SubscriptionUtils.chargesQuery({isManual: false, merchantName: '  '})).toBeNull();
  });
});
