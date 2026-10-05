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

  it('setPage moves to the page and never below the first', () => {
    const {state, methods} = setup([], 0);
    methods.setPage(4);
    expect(state.currentPage()).toBe(4);
    methods.setPage(0);
    expect(state.currentPage()).toBe(1);
  });

  it('setPageSize returns to the first page', () => {
    const {state, methods} = setup([], 0, {currentPage: 3});
    methods.setPageSize(50);
    expect(state.pageSize()).toBe(50);
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

  it('dismissLocal steps back a page when it empties a later page', () => {
    const {state, methods} = setup([makeAlert({id: 'only'})], 1, {currentPage: 3});
    methods.dismissLocal('only');
    expect(state.currentPage()).toBe(2);
  });

  it('dismissLocal stays on the first page when it empties the list', () => {
    const {state, methods} = setup([makeAlert({id: 'only'})], 1);
    methods.dismissLocal('only');
    expect(state.currentPage()).toBe(1);
  });
});
