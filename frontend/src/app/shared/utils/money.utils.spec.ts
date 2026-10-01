import {describe, expect, it} from 'vitest';

import {MoneyUtils} from './money.utils';

describe('MoneyUtils.format', () => {
  it('renders USD symbol-first with two decimals', () => {
    expect(MoneyUtils.format(18981.48, 'USD')).toBe('$18,981.48');
  });

  it('defaults to USD', () => {
    expect(MoneyUtils.format(5)).toBe('$5.00');
    expect(MoneyUtils.format(5, null)).toBe('$5.00');
  });

  it.each([
    ['EUR', '€10.00'],
    ['GBP', '£10.00'],
    ['UAH', '₴10.00'],
  ])('uses the unambiguous symbol for %s', (code, expected) => {
    expect(MoneyUtils.format(10, code)).toBe(expected);
  });

  it('falls back to the ISO code when the symbol is ambiguous', () => {
    expect(MoneyUtils.format(950, 'CHF')).toBe('CHF 950.00');
    expect(MoneyUtils.format(1.5, 'btc')).toBe('BTC 1.50');
  });

  it('puts the minus sign before the symbol', () => {
    expect(MoneyUtils.format(-1200, 'EUR')).toBe('-€1,200.00');
    expect(MoneyUtils.format(-3, 'CHF')).toBe('-CHF 3.00');
  });

  it('does not render negative zero', () => {
    expect(MoneyUtils.format(-0.001, 'USD')).toBe('$0.00');
  });

  it('prefixes positive amounts when signed', () => {
    expect(MoneyUtils.format(12.5, 'USD', {signed: true})).toBe('+$12.50');
    expect(MoneyUtils.format(-12.5, 'USD', {signed: true})).toBe('-$12.50');
    expect(MoneyUtils.format(0, 'USD', {signed: true})).toBe('$0.00');
  });

  it('honours max fraction digits and lowers the minimum with it', () => {
    expect(MoneyUtils.format(1234.56, 'USD', {maxFractionDigits: 0})).toBe('$1,235');
  });

  it('trims cents when only the minimum is lowered', () => {
    expect(MoneyUtils.format(1234, 'USD', {minFractionDigits: 0, maxFractionDigits: 2})).toBe(
      '$1,234'
    );
    expect(MoneyUtils.format(1234.5, 'USD', {minFractionDigits: 0, maxFractionDigits: 2})).toBe(
      '$1,234.5'
    );
  });

  it.each([null, undefined, Number.NaN])('renders %s as an em dash', value => {
    expect(MoneyUtils.format(value, 'USD')).toBe('—');
  });
});

describe('MoneyUtils.formatEquivalent', () => {
  it('prefixes whole-unit amounts with a tilde', () => {
    expect(MoneyUtils.formatEquivalent(1234.56)).toBe('~ $1,235');
  });

  it('honours the currency', () => {
    expect(MoneyUtils.formatEquivalent(500, 'EUR')).toBe('~ €500');
  });
});
