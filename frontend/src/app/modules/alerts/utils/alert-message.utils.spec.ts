import {describe, expect, it} from 'vitest';

import {AlertMessageUtils} from './alert-message.utils';

describe('AlertMessageUtils.roundNumbers', () => {
  it('rounds long decimals to two places', () => {
    expect(AlertMessageUtils.roundNumbers('Balance 1234.56789 EUR')).toBe('Balance 1234.57 EUR');
  });

  it('rounds every long decimal in the text', () => {
    expect(AlertMessageUtils.roundNumbers('1.23456 of 9.87654')).toBe('1.23 of 9.88');
  });

  it('leaves short decimals and integers untouched', () => {
    expect(AlertMessageUtils.roundNumbers('Spent 12.5 of 100')).toBe('Spent 12.5 of 100');
  });

  it('returns an empty string for null and empty input', () => {
    expect(AlertMessageUtils.roundNumbers(null)).toBe('');
    expect(AlertMessageUtils.roundNumbers('')).toBe('');
  });
});
