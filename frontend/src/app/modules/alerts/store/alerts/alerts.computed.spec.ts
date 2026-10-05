import {signal} from '@angular/core';
import {describe, expect, it} from 'vitest';

import {alertsComputed} from './alerts.computed';

function build(totalCount: number, currentPage: number, pageSize = 20) {
  return alertsComputed({
    alerts: signal([]),
    filter: signal('unread' as const),
    unreadCount: signal(0),
    status: signal('idle' as const),
    totalCount: signal(totalCount),
    currentPage: signal(currentPage),
    pageSize: signal(pageSize),
  });
}

describe('alertsComputed paging', () => {
  it('totalPages rounds up and is at least one', () => {
    expect(build(0, 1).totalPages()).toBe(1);
    expect(build(20, 1).totalPages()).toBe(1);
    expect(build(21, 1).totalPages()).toBe(2);
    expect(build(101, 1, 50).totalPages()).toBe(3);
  });

  it('knows whether there is a previous and a next page', () => {
    const first = build(45, 1);
    expect(first.hasPreviousPage()).toBe(false);
    expect(first.hasNextPage()).toBe(true);

    const last = build(45, 3);
    expect(last.hasPreviousPage()).toBe(true);
    expect(last.hasNextPage()).toBe(false);
  });

  it('query bundles the filter, page and page size the list is showing', () => {
    expect(build(45, 2, 50).query()).toEqual({filter: 'unread', page: 2, pageSize: 50});
  });
});
