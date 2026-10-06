import {describe, expect, it} from 'vitest';

import {PercentUtils} from './percent.utils';

describe('PercentUtils.fromFraction', () => {
  it('renders a whole percent without decimals', () => {
    expect(PercentUtils.fromFraction(0.04)).toBe('4');
  });

  it('keeps a fractional percent', () => {
    expect(PercentUtils.fromFraction(0.035)).toBe('3.5');
  });

  it('absorbs floating-point noise', () => {
    expect(PercentUtils.fromFraction(0.07)).toBe('7');
  });

  it('renders zero as 0', () => {
    expect(PercentUtils.fromFraction(0)).toBe('0');
  });
});

describe('PercentUtils.toFraction', () => {
  it('converts a typed percent to a fraction', () => {
    expect(PercentUtils.toFraction('4', 0.5, 10)).toBe(0.04);
  });

  it('accepts a decimal percent and surrounding whitespace', () => {
    expect(PercentUtils.toFraction(' 3.5 ', 0.5, 10)).toBe(0.035);
  });

  it('accepts the bounds themselves', () => {
    expect(PercentUtils.toFraction('0.5', 0.5, 10)).toBe(0.005);
    expect(PercentUtils.toFraction('10', 0.5, 10)).toBe(0.1);
  });

  it('rejects empty text', () => {
    expect(PercentUtils.toFraction('', 0.5, 10)).toBeNull();
    expect(PercentUtils.toFraction('   ', 0.5, 10)).toBeNull();
  });

  it('rejects text that is not a number', () => {
    expect(PercentUtils.toFraction('abc', 0.5, 10)).toBeNull();
  });

  it('rejects a value below the minimum', () => {
    expect(PercentUtils.toFraction('0.1', 0.5, 10)).toBeNull();
  });

  it('rejects a value above the maximum', () => {
    expect(PercentUtils.toFraction('11', 0.5, 10)).toBeNull();
  });
});
