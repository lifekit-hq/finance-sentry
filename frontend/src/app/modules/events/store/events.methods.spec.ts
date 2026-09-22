import {signalState} from '@ngrx/signals';
import {describe, expect, it} from 'vitest';

import {type FiredEvent, type FiredEventsPageResponse} from '../models/event/event.model';
import {eventsMethods} from './events.methods';
import {initialEventsState} from './events.state';

function fired(alertId: string): FiredEvent {
  return {
    alertId,
    kind: 'NewsCluster',
    severity: 'Warning',
    subject: 'MU',
    title: 'News cluster: MU',
    message: 'm',
    occurredAt: '2026-09-22T10:00:00Z',
    isRead: false,
    delivery: null,
    verdict: null,
    outcome: 'awaiting',
  };
}

function page(
  items: FiredEvent[],
  pageNumber: number,
  totalCount: number
): FiredEventsPageResponse {
  return {items, totalCount, page: pageNumber, pageSize: 20, totalPages: 2};
}

describe('eventsMethods', () => {
  it('toggleKindLocal adds then removes a kind', () => {
    const state = signalState(initialEventsState);
    const methods = eventsMethods(state);

    methods.toggleKindLocal('macro');
    expect(state.kinds()).toEqual(['macro']);

    methods.toggleKindLocal('earnings');
    expect(state.kinds()).toEqual(['macro', 'earnings']);

    methods.toggleKindLocal('macro');
    expect(state.kinds()).toEqual(['earnings']);
  });

  it('setUpcoming stores items and sources and clears the error', () => {
    const state = signalState({
      ...initialEventsState,
      upcomingStatus: 'error' as const,
      upcomingErrorCode: 'X',
    });
    const methods = eventsMethods(state);

    methods.setUpcoming({
      items: [],
      from: '2026-09-22',
      to: '2026-10-22',
      sources: [{source: 'macro', status: 'unavailable'}],
    });

    expect(state.upcomingStatus()).toBe('idle');
    expect(state.upcomingErrorCode()).toBeNull();
    expect(state.sources()).toEqual([{source: 'macro', status: 'unavailable'}]);
  });

  it('setUpcomingError flips the status and keeps the code', () => {
    const state = signalState(initialEventsState);
    eventsMethods(state).setUpcomingError('EVENTS_WINDOW_INVALID');

    expect(state.upcomingStatus()).toBe('error');
    expect(state.upcomingErrorCode()).toBe('EVENTS_WINDOW_INVALID');
  });

  it('setFired replaces the feed while appendFired extends it and advances the page', () => {
    const state = signalState(initialEventsState);
    const methods = eventsMethods(state);

    methods.setFired(page([fired('a')], 1, 3));
    expect(state.fired().map(f => f.alertId)).toEqual(['a']);
    expect(state.firedTotalCount()).toBe(3);

    methods.appendFired(page([fired('b'), fired('c')], 2, 3));
    expect(state.fired().map(f => f.alertId)).toEqual(['a', 'b', 'c']);
    expect(state.firedPage()).toBe(2);
    expect(state.firedStatus()).toBe('idle');
  });

  it('appendFired skips rows already loaded when a page overlaps', () => {
    const state = signalState(initialEventsState);
    const methods = eventsMethods(state);

    methods.setFired(page([fired('a'), fired('b')], 1, 4));
    methods.appendFired(page([fired('b'), fired('c')], 2, 4));

    expect(state.fired().map(f => f.alertId)).toEqual(['a', 'b', 'c']);
    expect(state.firedPage()).toBe(2);
  });

  it('setView and setHorizonDays patch their fields only', () => {
    const state = signalState(initialEventsState);
    const methods = eventsMethods(state);

    methods.setView('fired');
    methods.setHorizonDays(90);

    expect(state.view()).toBe('fired');
    expect(state.horizonDays()).toBe(90);
    expect(state.kinds()).toEqual([]);
  });
});
