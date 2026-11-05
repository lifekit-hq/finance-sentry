import {describe, expect, it} from 'vitest';

import {ColourAlphaUtils} from './colour-alpha.utils';

describe('ColourAlphaUtils.withAlpha', () => {
  it('converts a six-digit hex', () => {
    expect(ColourAlphaUtils.withAlpha('#00673c', 0.5)).toBe('rgba(0, 103, 60, 0.5)');
  });

  it('converts a three-digit hex', () => {
    expect(ColourAlphaUtils.withAlpha('#fa0', 0.25)).toBe('rgba(255, 170, 0, 0.25)');
  });

  it('reads an rgb or rgba colour', () => {
    expect(ColourAlphaUtils.withAlpha('rgb(10, 20, 30)', 0.1)).toBe('rgba(10, 20, 30, 0.1)');
    expect(ColourAlphaUtils.withAlpha('rgba(10 20 30 / 0.9)', 0.1)).toBe('rgba(10, 20, 30, 0.1)');
  });

  it('trims whitespace', () => {
    expect(ColourAlphaUtils.withAlpha('  #ffffff ', 1)).toBe('rgba(255, 255, 255, 1)');
  });

  it('returns a colour it cannot parse unchanged', () => {
    expect(ColourAlphaUtils.withAlpha('currentColor', 0.5)).toBe('currentColor');
  });
});
