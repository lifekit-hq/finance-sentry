import {signalState} from '@ngrx/signals';
import {describe, expect, it} from 'vitest';

import {type Alert} from '../../models/alert/alert.model';
import {alertsMethods} from './alerts.methods';
import {initialAlertsState} from './alerts.state';

function makeAlert(overrides: Partial<Alert> = {}): Alert {
  return {
    id: 'a1',
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
    ...overrides,
  };
}

function setup(alerts: Alert[], unreadCount: number, overrides = {}) {
  const state = signalState({
    ...initialAlertsState,
    alerts,
    totalCount: alerts.length,
    unreadCount,
    ...overrides,
  });
  return {state, methods: alertsMethods(state)};
}

describe('alertsMethods', () => {
  it('setFilter returns to the first page', () => {
    const {state, methods} = setup([], 0, {currentPage: 3});
    methods.setFilter('error');
    expect(state.filter()).toBe('error');
    expect(state.currentPage()).toBe(1);
  });

  it('setSearch returns to the first page', () => {
    const {state, methods} = setup([], 0, {currentPage: 3});
    methods.setSearch('rent');
    expect(state.search()).toBe('rent');
    expect(state.currentPage()).toBe(1);
  });

  it('markReadLocal lowers the badge for an open unread alert', () => {
    const {state, methods} = setup([makeAlert({id: 'a'})], 5);
    methods.markReadLocal('a');
    expect(state.alerts()[0].isRead).toBe(true);
    expect(state.unreadCount()).toBe(4);
  });

  it('markReadLocal leaves the badge alone for a resolved alert (it was never counted)', () => {
    const {state, methods} = setup([makeAlert({id: 'a', isResolved: true})], 5);
    methods.markReadLocal('a');
    expect(state.alerts()[0].isRead).toBe(true);
    expect(state.unreadCount()).toBe(5);
  });

  it('markReadLocal drops the row from the Unread filter and shrinks the total', () => {
    const {state, methods} = setup([makeAlert({id: 'a'}), makeAlert({id: 'b'})], 2, {
      filter: 'unread',
    });
    methods.markReadLocal('a');
    expect(state.alerts().map(a => a.id)).toEqual(['b']);
    expect(state.totalCount()).toBe(1);
    expect(state.unreadCount()).toBe(1);
  });

  it('markAllReadLocal empties the Unread filter', () => {
    const {state, methods} = setup([makeAlert({id: 'a'}), makeAlert({id: 'b'})], 2, {
      filter: 'unread',
      currentPage: 2,
    });
    methods.markAllReadLocal();
    expect(state.alerts()).toEqual([]);
    expect(state.totalCount()).toBe(0);
    expect(state.unreadCount()).toBe(0);
    expect(state.currentPage()).toBe(1);
  });

  it('markAllReadLocal keeps rows, marked read, on other filters', () => {
    const {state, methods} = setup([makeAlert({id: 'a'})], 1);
    methods.markAllReadLocal();
    expect(state.alerts().map(a => a.isRead)).toEqual([true]);
    expect(state.totalCount()).toBe(1);
  });

  it('dismissLocal drops the row and lowers the badge only for an open unread alert', () => {
    const {state, methods} = setup(
      [makeAlert({id: 'open'}), makeAlert({id: 'done', isResolved: true})],
      3
    );
    methods.dismissLocal('done');
    expect(state.alerts().map(a => a.id)).toEqual(['open']);
    expect(state.totalCount()).toBe(1);
    expect(state.unreadCount()).toBe(3);

    methods.dismissLocal('open');
    expect(state.unreadCount()).toBe(2);
  });

  it('setData replaces the rows and returns to the first page', () => {
    const {state, methods} = setup([makeAlert({id: 'old'})], 0, {currentPage: 3});
    methods.setData([makeAlert({id: 'new'})], 9, 4);
    expect(state.alerts().map(a => a.id)).toEqual(['new']);
    expect(state.totalCount()).toBe(9);
    expect(state.unreadCount()).toBe(4);
    expect(state.currentPage()).toBe(1);
  });

  it('appendData adds the next page once, skipping a row already on screen', () => {
    const {state, methods} = setup([makeAlert({id: 'a'})], 1);
    methods.appendData([makeAlert({id: 'a'}), makeAlert({id: 'b'})], 30, 2);
    expect(state.alerts().map(a => a.id)).toEqual(['a', 'b']);
    expect(state.totalCount()).toBe(30);
    expect(state.unreadCount()).toBe(2);
    expect(state.currentPage()).toBe(2);
  });

  it('clearData drops the rows of a query that failed to reload', () => {
    const {state, methods} = setup([makeAlert({id: 'a'})], 1, {currentPage: 2});
    methods.clearData();
    expect(state.alerts()).toEqual([]);
    expect(state.totalCount()).toBe(0);
    expect(state.currentPage()).toBe(1);
  });
});
