import {describe, expect, it} from 'vitest';

import {NetBalancePipe} from './net-balance.pipe';

describe('NetBalancePipe', () => {
  const pipe = new NetBalancePipe();

  it('labels a negative total as owed', () => {
    expect(pipe.transform(-917.82, 'USD')).toBe('Owes $917.82');
  });

  it('formats a positive total as money', () => {
    expect(pipe.transform(18981.48, 'USD')).toBe('$18,981.48');
  });
});
