import {HttpErrorResponse} from '@angular/common/http';
import {TestBed} from '@angular/core/testing';
import {ERROR_MESSAGES} from '@lifekit-hq/core';
import {NEVER, of, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {ERROR_MESSAGES_REGISTRY} from '../../../../core/errors/error-messages.registry';
import {type CareerEventInput} from '../../models/career-event/career-event.model';
import {CareerEventsService} from '../../services/career-events.service';
import {FUTURE_RAISE} from './career-events.fixtures';
import {CareerEventsStore} from './career-events.store';

const INPUT: CareerEventInput = {
  kind: 'IncomeStep',
  date: '2027-03-01',
  endDate: null,
  label: 'Promotion',
  amount: 500,
  currency: 'EUR',
  note: null,
};

function apiError(errorCode: string) {
  return throwError(() => new HttpErrorResponse({status: 404, error: {errorCode}}));
}

describe('CareerEventsStore', () => {
  let service: {
    list: ReturnType<typeof vi.fn>;
    create: ReturnType<typeof vi.fn>;
    update: ReturnType<typeof vi.fn>;
    retire: ReturnType<typeof vi.fn>;
    remove: ReturnType<typeof vi.fn>;
  };

  beforeEach(() => {
    service = {
      list: vi.fn().mockReturnValue(of([FUTURE_RAISE])),
      create: vi.fn().mockReturnValue(of(FUTURE_RAISE)),
      update: vi.fn().mockReturnValue(of(FUTURE_RAISE)),
      retire: vi.fn().mockReturnValue(of(FUTURE_RAISE)),
      remove: vi.fn().mockReturnValue(of(undefined)),
    };
    TestBed.configureTestingModule({
      providers: [
        CareerEventsStore,
        {provide: CareerEventsService, useValue: service},
        {provide: ERROR_MESSAGES, useValue: ERROR_MESSAGES_REGISTRY},
      ],
    });
  });

  it('loads on init and reads as loading until the answer arrives', () => {
    service.list.mockReturnValue(NEVER);

    const store = TestBed.inject(CareerEventsStore);

    expect(store.isLoading()).toBe(true);
  });

  it('stores the events and settles on success', () => {
    const store = TestBed.inject(CareerEventsStore);

    expect(store.events()).toEqual([FUTURE_RAISE]);
    expect(store.eventsStatus()).toBe('success');
    expect(store.rows()).toHaveLength(1);
  });

  it('reports a failed load with the flow fallback', () => {
    service.list.mockReturnValue(apiError('UNKNOWN'));

    const store = TestBed.inject(CareerEventsStore);

    expect(store.eventsStatus()).toBe('error');
    expect(store.eventsErrorMessage()).toBe('Failed to load the career timeline.');
  });

  it('creates a new event and reloads', () => {
    const store = TestBed.inject(CareerEventsStore);
    service.list.mockClear();

    store.save({id: null, input: INPUT});

    expect(service.create).toHaveBeenCalledWith(INPUT);
    expect(service.list).toHaveBeenCalledTimes(1);
    expect(store.saveStatus()).toBe('success');
  });

  it('updates an existing event', () => {
    const store = TestBed.inject(CareerEventsStore);

    store.save({id: 'e-1', input: INPUT});

    expect(service.update).toHaveBeenCalledWith('e-1', INPUT);
  });

  it('shows a save error from the API', () => {
    service.create.mockReturnValue(apiError('CAREER_EVENT_NOT_FOUND'));
    const store = TestBed.inject(CareerEventsStore);

    store.save({id: null, input: INPUT});

    expect(store.saveStatus()).toBe('error');
    expect(store.saveErrorMessage()).toBe(ERROR_MESSAGES_REGISTRY['CAREER_EVENT_NOT_FOUND']);
  });

  it('retires an event and reloads', () => {
    const store = TestBed.inject(CareerEventsStore);
    service.list.mockClear();

    store.retire('e-1');

    expect(service.retire).toHaveBeenCalledWith('e-1');
    expect(service.list).toHaveBeenCalledTimes(1);
    expect(store.busyId()).toBeNull();
  });

  it('deletes an event and reloads', () => {
    const store = TestBed.inject(CareerEventsStore);
    service.list.mockClear();

    store.remove('e-1');

    expect(service.remove).toHaveBeenCalledWith('e-1');
    expect(service.list).toHaveBeenCalledTimes(1);
  });

  it('surfaces a failed retire as an action error', () => {
    service.retire.mockReturnValue(apiError('CAREER_EVENT_NOT_FOUND'));
    const store = TestBed.inject(CareerEventsStore);

    store.retire('e-1');

    expect(store.busyId()).toBeNull();
    expect(store.actionErrorMessage()).toBe(ERROR_MESSAGES_REGISTRY['CAREER_EVENT_NOT_FOUND']);
  });

  it('surfaces a failed delete as an action error', () => {
    service.remove.mockReturnValue(apiError('CAREER_EVENT_NOT_FOUND'));
    const store = TestBed.inject(CareerEventsStore);

    store.remove('e-1');

    expect(store.actionErrorMessage()).toBe(ERROR_MESSAGES_REGISTRY['CAREER_EVENT_NOT_FOUND']);
  });
});
