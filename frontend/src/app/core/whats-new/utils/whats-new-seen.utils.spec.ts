import {afterEach, beforeEach, describe, expect, it, vi} from 'vitest';

import {WHATS_NEW_SEEN_STORAGE_KEY} from '../constants/whats-new.constants';
import {WhatsNewSeenUtils} from './whats-new-seen.utils';

describe('WhatsNewSeenUtils', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('reads back what it wrote', () => {
    WhatsNewSeenUtils.write('1.2.3');

    expect(localStorage.getItem(WHATS_NEW_SEEN_STORAGE_KEY)).toBe('1.2.3');
    expect(WhatsNewSeenUtils.read()).toBe('1.2.3');
  });

  it('reads null when nothing is stored', () => {
    expect(WhatsNewSeenUtils.read()).toBeNull();
  });

  it('survives blocked storage', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });

    expect(WhatsNewSeenUtils.read()).toBeNull();
    expect(() => WhatsNewSeenUtils.write('1.2.3')).not.toThrow();
  });
});
