import {describe, expect, it} from 'vitest';

import {EventTitleUtils} from './event-title.utils';

describe('EventTitleUtils.rowTitle', () => {
  it('returns an empty title when only the subject remains after the kind prefix', () => {
    expect(EventTitleUtils.rowTitle('Earnings: MU', 'Earnings', 'MU')).toBe('');
  });

  it('matches the prefix and subject case-insensitively', () => {
    expect(EventTitleUtils.rowTitle('earnings: mu', 'Earnings', 'MU')).toBe('');
  });

  it('keeps the remainder when it differs from the subject', () => {
    expect(EventTitleUtils.rowTitle('Earnings: MU Q3 call', 'Earnings', 'MU')).toBe('MU Q3 call');
  });

  it('leaves titles of other shapes unchanged', () => {
    expect(EventTitleUtils.rowTitle('CPI (Sep)', 'Macro', 'US')).toBe('CPI (Sep)');
    expect(EventTitleUtils.rowTitle('News cluster: x', 'Earnings', 'MU')).toBe('News cluster: x');
  });

  it('keeps the title when nothing would remain after the prefix', () => {
    expect(EventTitleUtils.rowTitle('Earnings: ', 'Earnings', 'MU')).toBe('Earnings: ');
  });
});
