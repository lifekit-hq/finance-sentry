import {describe, expect, it} from 'vitest';

import {EventTitleUtils} from './event-title.utils';

describe('EventTitleUtils.stripKindPrefix', () => {
  it('strips the duplicated kind prefix', () => {
    expect(EventTitleUtils.stripKindPrefix('Earnings: MU', 'Earnings')).toBe('MU');
  });

  it('matches the prefix case-insensitively', () => {
    expect(EventTitleUtils.stripKindPrefix('earnings: MU', 'Earnings')).toBe('MU');
  });

  it('leaves titles of other shapes unchanged', () => {
    expect(EventTitleUtils.stripKindPrefix('CPI (Sep)', 'Macro')).toBe('CPI (Sep)');
    expect(EventTitleUtils.stripKindPrefix('News cluster: x', 'Earnings')).toBe('News cluster: x');
  });

  it('keeps the title when nothing would remain', () => {
    expect(EventTitleUtils.stripKindPrefix('Earnings: ', 'Earnings')).toBe('Earnings: ');
  });
});
