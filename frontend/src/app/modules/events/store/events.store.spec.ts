import {HttpErrorResponse} from '@angular/common/http';
import {TestBed} from '@angular/core/testing';
import {ERROR_MESSAGES} from '@lifekit-hq/core';
import {NEVER, of, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {ERROR_MESSAGES_REGISTRY} from '../../../core/errors/error-messages.registry';
import {
  type FiredEvent,
  type FiredEventsPageResponse,
  type UpcomingEventsResult,
} from '../models/event/event.model';
import {EventsService} from '../services/events.service';
import {EventsStore} from './events.store';

const UPCOMING: UpcomingEventsResult = {
  items: [],
  from: '2026-09-22',
  to: '2026-10-22',
  sources: [],
};

function firedEvent(alertId: string): FiredEvent {
  return {
    alertId,
    kind: 'NewsCluster',
    severity: 'Warning',
    subject: 'MU',
    title: 't',
    message: 'm',
    occurredAt: '2026-09-22T10:00:00Z',
    isRead: false,
    delivery: null,
    verdict: null,
    outcome: 'awaiting',
  };
}

function firedPage(items: FiredEvent[], page: number): FiredEventsPageResponse {
  return {items, totalCount: 2, page, pageSize: 1, totalPages: 2};
}

describe('EventsStore', () => {
  let api: {getUpcoming: ReturnType<typeof vi.fn>; getFired: ReturnType<typeof vi.fn>};

  function inject(): InstanceType<typeof EventsStore> {
    TestBed.configureTestingModule({
      providers: [
        EventsStore,
        {provide: EventsService, useValue: api},
        {provide: ERROR_MESSAGES, useValue: ERROR_MESSAGES_REGISTRY},
      ],
    });
    return TestBed.inject(EventsStore);
  }

  beforeEach(() => {
    TestBed.resetTestingModule();
    api = {
      getUpcoming: vi.fn().mockReturnValue(of(UPCOMING)),
      getFired: vi.fn().mockReturnValue(of(firedPage([firedEvent('a')], 1))),
    };
  });

  it('loads both feeds on init and reports success', () => {
    const store = inject();

    expect(store.upcomingStatus()).toBe('success');
    expect(store.fired().map(f => f.alertId)).toEqual(['a']);
    expect(store.firedStatus()).toBe('success');
    expect(store.isUpcomingEmpty()).toBe(true);
    expect(store.isFiredEmpty()).toBe(false);
  });

  it('is loading until each feed answers', () => {
    api.getUpcoming.mockReturnValue(NEVER);
    api.getFired.mockReturnValue(NEVER);

    const store = inject();

    expect(store.isUpcomingLoading()).toBe(true);
    expect(store.isFiredLoading()).toBe(true);
    expect(store.isUpcomingEmpty()).toBe(false);
    expect(store.isFiredEmpty()).toBe(false);
  });

  it('keeps the two feeds independent when one fails', () => {
    api.getUpcoming.mockReturnValue(
      throwError(
        () => new HttpErrorResponse({status: 400, error: {errorCode: 'EVENTS_WINDOW_INVALID'}})
      )
    );

    const store = inject();

    expect(store.upcomingStatus()).toBe('error');
    expect(store.upcomingErrorMessage()).toContain('window is invalid');
    expect(store.firedStatus()).toBe('success');
    expect(store.firedErrorMessage()).toBe('');
  });

  it('falls back to the feed defaults for an unregistered code', () => {
    api.getUpcoming.mockReturnValue(
      throwError(() => new HttpErrorResponse({status: 500, error: {errorCode: 'NOPE'}}))
    );
    api.getFired.mockReturnValue(
      throwError(() => new HttpErrorResponse({status: 500, error: {errorCode: 'NOPE'}}))
    );

    const store = inject();

    expect(store.upcomingErrorMessage()).toBe('Failed to load upcoming events.');
    expect(store.firedErrorMessage()).toBe('Failed to load fired events.');
  });

  it('keeps fired rows under the error when loading the next page fails', () => {
    const store = inject();
    api.getFired.mockReturnValue(
      throwError(() => new HttpErrorResponse({status: 500, error: {errorCode: 'NOPE'}}))
    );

    store.loadMoreFired();

    expect(store.firedStatus()).toBe('error');
    expect(store.hasFiredRows()).toBe(true);
    expect(store.firedErrorMessage()).toBe('Failed to load fired events.');
  });

  it('appends the next page and returns to success', () => {
    const store = inject();
    api.getFired.mockReturnValue(of(firedPage([firedEvent('b')], 2)));

    store.loadMoreFired();

    expect(store.fired().map(f => f.alertId)).toEqual(['a', 'b']);
    expect(store.firedPage()).toBe(2);
    expect(store.firedStatus()).toBe('success');
  });
});
