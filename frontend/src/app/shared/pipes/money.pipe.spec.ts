import {describe, expect, it} from 'vitest';

import {MoneyPipe} from './money.pipe';

describe('MoneyPipe', () => {
  const pipe = new MoneyPipe();

  it('delegates to MoneyUtils', () => {
    expect(pipe.transform(1234.5, 'EUR')).toBe('€1,234.50');
  });

  it('forwards format options', () => {
    expect(pipe.transform(10, 'USD', {signed: true})).toBe('+$10.00');
  });
});
