import {describe, expect, it} from 'vitest';

import {GapModeUtils} from './gap-mode.utils';

describe('GapModeUtils', () => {
  it('reads a name that is not held as missing', () => {
    expect(GapModeUtils.gapText({status: 'Missing', gapPct: 12})).toBe('missing');
  });

  it('reads a gap inside the tolerance as on target', () => {
    expect(GapModeUtils.gapText({status: 'OnTarget', gapPct: 0.3})).toBe('on target');
  });

  it('reads a positive gap as a shortfall in points', () => {
    expect(GapModeUtils.gapText({status: 'Underweight', gapPct: 3.1})).toBe('short by 3.1 pts');
  });

  it('reads a negative gap as an excess in points, without the sign', () => {
    expect(GapModeUtils.gapText({status: 'Overweight', gapPct: -13})).toBe('over by 13 pts');
  });

  it('trims the points to two decimals', () => {
    expect(GapModeUtils.points(-2.014)).toBe('2.01');
    expect(GapModeUtils.points(4)).toBe('4');
  });
});
