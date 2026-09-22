import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {of, Subject, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {
  type EventKind,
  type FiredEventsPageResponse,
  type UpcomingEventsResult,
} from '../models/event/event.model';
import {EventsService} from '../services/events.service';
import {eventsEffects} from './events.effects';

const UPCOMING: UpcomingEventsResult = {
  items: [],
  from: '2026-09-22',
  to: '2026-10-22',
  sources: [],
};
const FIRED: FiredEventsPageResponse = {
  items: [],
  totalCount: 0,
  page: 1,
  pageSize: 20,
  totalPages: 0,
};

function buildStore(horizonDays = 30) {
  const window = signal({from: '2026-09-22', to: `2026-10-${horizonDays === 30 ? '22' : '29'}`});
  return {
    kinds: signal<EventKind[]>([]),
    calendarWindow: window,
    firedPage: signal(1),
    firedPageSize: signal(20),
    setHorizonDays: vi.fn((days: number) => {
      window.set({from: '2026-09-22', to: days === 90 ? '2026-12-21' : '2026-10-22'});
    }),
    toggleKindLocal: vi.fn(),
    setUpcomingLoading: vi.fn(),
    setUpcoming: vi.fn(),
    setUpcomingError: vi.fn(),
    setFiredLoading: vi.fn(),
    setFired: vi.fn(),
    appendFired: vi.fn(),
    setFiredError: vi.fn(),
  };
}

function buildApi() {
  return {
    getUpcoming: vi.fn().mockReturnValue(of(UPCOMING)),
    getFired: vi.fn().mockReturnValue(of(FIRED)),
  };
}

function configure(api: ReturnType<typeof buildApi>): void {
  TestBed.configureTestingModule({providers: [{provide: EventsService, useValue: api}]});
}

describe('eventsEffects', () => {
  beforeEach(() => {
    TestBed.resetTestingModule();
  });

  it('loadUpcoming asks for the calendar window and stores the result', () => {
    const store = buildStore();
    const api = buildApi();
    configure(api);

    TestBed.runInInjectionContext(() => eventsEffects(store).loadUpcoming());

    expect(store.setUpcomingLoading).toHaveBeenCalledOnce();
    expect(api.getUpcoming).toHaveBeenCalledWith('2026-09-22', '2026-10-22', []);
    expect(store.setUpcoming).toHaveBeenCalledWith(UPCOMING);
  });

  it('selectHorizon re-queries with the new window end', () => {
    const store = buildStore();
    const api = buildApi();
    configure(api);

    TestBed.runInInjectionContext(() => eventsEffects(store).selectHorizon(90));

    expect(store.setHorizonDays).toHaveBeenCalledWith(90);
    expect(api.getUpcoming).toHaveBeenCalledWith('2026-09-22', '2026-12-21', []);
  });

  it('toggleKind narrows the selection then re-queries', () => {
    const store = buildStore();
    store.kinds.set(['macro']);
    const api = buildApi();
    configure(api);

    TestBed.runInInjectionContext(() => eventsEffects(store).toggleKind('macro'));

    expect(store.toggleKindLocal).toHaveBeenCalledWith('macro');
    expect(api.getUpcoming).toHaveBeenCalledWith('2026-09-22', '2026-10-22', ['macro']);
  });

  it('a filter change cancels the calendar request still in flight', () => {
    const store = buildStore();
    const slow = new Subject<UpcomingEventsResult>();
    const fast = new Subject<UpcomingEventsResult>();
    const api = buildApi();
    api.getUpcoming.mockReturnValueOnce(slow).mockReturnValueOnce(fast);
    configure(api);

    TestBed.runInInjectionContext(() => {
      const effects = eventsEffects(store);
      effects.selectHorizon(90);
      effects.toggleKind('macro');
    });
    const macroOnly: UpcomingEventsResult = {...UPCOMING, to: '2026-12-21'};
    fast.next(macroOnly);
    fast.complete();
    slow.next(UPCOMING);
    slow.complete();

    expect(store.setUpcoming).toHaveBeenCalledOnce();
    expect(store.setUpcoming).toHaveBeenCalledWith(macroOnly);
  });

  it('loadUpcoming forwards the backend error code', () => {
    const store = buildStore();
    const api = buildApi();
    api.getUpcoming.mockReturnValue(
      throwError(() => ({error: {errorCode: 'EVENTS_WINDOW_INVALID'}}))
    );
    configure(api);

    TestBed.runInInjectionContext(() => eventsEffects(store).loadUpcoming());

    expect(store.setUpcomingError).toHaveBeenCalledWith('EVENTS_WINDOW_INVALID');
    expect(store.setUpcoming).not.toHaveBeenCalled();
  });

  it('loadFired fetches the first page and loadMoreFired the next one', () => {
    const store = buildStore();
    store.firedPage.set(2);
    const api = buildApi();
    configure(api);

    TestBed.runInInjectionContext(() => {
      const effects = eventsEffects(store);
      effects.loadFired();
      effects.loadMoreFired();
    });

    expect(api.getFired).toHaveBeenNthCalledWith(1, 1, 20);
    expect(api.getFired).toHaveBeenNthCalledWith(2, 3, 20);
    expect(store.setFired).toHaveBeenCalledWith(FIRED);
    expect(store.appendFired).toHaveBeenCalledWith(FIRED);
  });

  it('loadFired forwards the backend error code', () => {
    const store = buildStore();
    const api = buildApi();
    api.getFired.mockReturnValue(throwError(() => ({error: {errorCode: 'BOOM'}})));
    configure(api);

    TestBed.runInInjectionContext(() => eventsEffects(store).loadFired());

    expect(store.setFiredError).toHaveBeenCalledWith('BOOM');
  });
});
