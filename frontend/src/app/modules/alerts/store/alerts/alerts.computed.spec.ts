import {signal} from '@angular/core';
import {describe, expect, it} from 'vitest';

import {alertsComputed} from './alerts.computed';
import {ALERTS_PAGE_SIZE} from './alerts.state';

function build(currentPage: number, totalCount: number) {
  return alertsComputed({
    currentPage: signal(currentPage),
    filter: signal('unread' as const),
    unreadCount: signal(0),
    totalCount: signal(totalCount),
    search: signal(' rent '),
  });
}

describe('alertsComputed paging', () => {
  it('has more while the loaded pages do not cover what the server holds', () => {
    expect(build(1, 45).hasMore()).toBe(true);
    expect(build(2, 45).hasMore()).toBe(true);
    expect(build(3, 45).hasMore()).toBe(false);
    expect(build(1, 0).hasMore()).toBe(false);
  });

  it('ends after the last page even when a local removal left the count short', () => {
    expect(build(2, 2 * ALERTS_PAGE_SIZE - 1).hasMore()).toBe(false);
  });

  it('query bundles the filter and trimmed search the list is showing', () => {
    expect(build(0, 45).query()).toEqual({filter: 'unread', search: 'rent'});
  });
});
