import {describe, expect, it} from 'vitest';

import {type AsyncViewInput} from '../models/async-view/async-view.model';
import {AsyncViewUtils, OFFLINE_ERROR_MESSAGE} from './async-view.utils';

const NOW = new Date(2026, 9, 7, 15, 0);
const base: AsyncViewInput = {
  isLoading: false,
  hasData: false,
  errorMessage: '',
  offline: false,
  lastSyncedAt: null,
};

describe('AsyncViewUtils.resolve', () => {
  it('shows a skeleton only while nothing is on screen', () => {
    expect(AsyncViewUtils.resolve({...base, isLoading: true}).status).toBe('loading');
  });

  it('keeps stale data while a retry loads', () => {
    const view = AsyncViewUtils.resolve({...base, isLoading: true, hasData: true});
    expect(view.status).toBe('success');
  });

  it('shows an error without data as an error, never as success or empty', () => {
    const view = AsyncViewUtils.resolve({...base, errorMessage: 'Boom'});
    expect(view).toEqual({status: 'error', errorMessage: 'Boom', offlineNotice: null});
  });

  it('keeps the data under the error when a refresh fails online', () => {
    const view = AsyncViewUtils.resolve({...base, hasData: true, errorMessage: 'Boom'});
    expect(view.status).toBe('error');
  });

  it('shows offline with data as success plus a last-synced notice', () => {
    const at = new Date(2026, 9, 7, 9, 5).getTime();
    const view = AsyncViewUtils.resolve(
      {...base, offline: true, hasData: true, errorMessage: 'Boom', lastSyncedAt: at},
      NOW
    );
    expect(view.status).toBe('success');
    expect(view.errorMessage).toBe('');
    expect(view.offlineNotice).toContain("You're offline");
    expect(view.offlineNotice).toContain('last sync (');
  });

  it('shows offline without data as an offline error, not empty', () => {
    const view = AsyncViewUtils.resolve({...base, offline: true, errorMessage: 'Boom'});
    expect(view).toEqual({
      status: 'error',
      errorMessage: OFFLINE_ERROR_MESSAGE,
      offlineNotice: null,
    });
  });

  it('is success when nothing is wrong', () => {
    expect(AsyncViewUtils.resolve({...base, hasData: true}).status).toBe('success');
  });
});

describe('AsyncViewUtils.lastSyncedLabel', () => {
  it('is empty without a sync time', () => {
    expect(AsyncViewUtils.lastSyncedLabel(null, NOW)).toBe('');
    expect(AsyncViewUtils.offlineNotice(null, NOW)).toBe(
      "You're offline. Showing the data from your last sync."
    );
  });

  it('shows the time for today and the date for an earlier day', () => {
    const today = AsyncViewUtils.lastSyncedLabel(new Date(2026, 9, 7, 9, 5).getTime(), NOW);
    const earlier = AsyncViewUtils.lastSyncedLabel(new Date(2026, 9, 5, 9, 5).getTime(), NOW);
    expect(today).toMatch(/9:05|09:05/);
    expect(earlier).not.toBe(today);
    expect(earlier).toMatch(/5/);
  });
});
