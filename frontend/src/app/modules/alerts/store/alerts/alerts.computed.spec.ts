import {signal} from '@angular/core';
import {describe, expect, it} from 'vitest';

import {type Alert} from '../../models/alert/alert.model';
import {alertsComputed} from './alerts.computed';

const BASE: Alert = {
  id: 'a',
  type: 'LowBalance',
  severity: 'Warning',
  title: 'Low balance',
  message: 'Balance dropped',
  referenceId: null,
  referenceLabel: null,
  isRead: false,
  isResolved: false,
  createdAt: '2026-10-01T09:00:00Z',
  resolvedAt: null,
  occurrenceCount: 1,
  lastOccurredAt: '2026-10-01T09:00:00Z',
};

function build(loaded: number, totalCount: number, exhausted = false) {
  return alertsComputed({
    alerts: signal(Array.from({length: loaded}, (_, i): Alert => ({...BASE, id: `a${i}`}))),
    exhausted: signal(exhausted),
    filter: signal('unread' as const),
    unreadCount: signal(0),
    totalCount: signal(totalCount),
    search: signal(' rent '),
  });
}

describe('alertsComputed paging', () => {
  it('has more while fewer alerts are loaded than the server holds', () => {
    expect(build(20, 45).hasMore()).toBe(true);
    expect(build(45, 45).hasMore()).toBe(false);
    expect(build(0, 0).hasMore()).toBe(false);
  });

  it('has no more once the server had nothing new to give, whatever the count says', () => {
    expect(build(38, 39, true).hasMore()).toBe(false);
  });

  it('query bundles the filter and trimmed search the list is showing', () => {
    expect(build(0, 45).query()).toEqual({filter: 'unread', search: 'rent'});
  });
});
