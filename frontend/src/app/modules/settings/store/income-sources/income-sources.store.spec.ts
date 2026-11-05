import {HttpErrorResponse} from '@angular/common/http';
import {TestBed} from '@angular/core/testing';
import {ERROR_MESSAGES} from '@lifekit-hq/core';
import {NEVER, of, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {ERROR_MESSAGES_REGISTRY} from '../../../../core/errors/error-messages.registry';
import {type IncomeSourceInput} from '../../models/income-source/income-source.model';
import {IncomeSourcesService} from '../../services/income-sources.service';
import {MONTHLY_SALARY} from './income-sources.fixtures';
import {IncomeSourcesStore} from './income-sources.store';

const INPUT: IncomeSourceInput = {
  kind: 'Salary',
  label: 'Day job',
  amount: 3000,
  currency: 'EUR',
  cadence: 'Monthly',
  grossOrNet: 'Net',
  startDate: '2026-01-01',
  endDate: null,
};

function apiError(errorCode: string) {
  return throwError(() => new HttpErrorResponse({status: 404, error: {errorCode}}));
}

describe('IncomeSourcesStore', () => {
  let service: {
    list: ReturnType<typeof vi.fn>;
    create: ReturnType<typeof vi.fn>;
    update: ReturnType<typeof vi.fn>;
    retire: ReturnType<typeof vi.fn>;
    remove: ReturnType<typeof vi.fn>;
  };

  beforeEach(() => {
    service = {
      list: vi.fn().mockReturnValue(of([MONTHLY_SALARY])),
      create: vi.fn().mockReturnValue(of(MONTHLY_SALARY)),
      update: vi.fn().mockReturnValue(of(MONTHLY_SALARY)),
      retire: vi.fn().mockReturnValue(of(MONTHLY_SALARY)),
      remove: vi.fn().mockReturnValue(of(undefined)),
    };
    TestBed.configureTestingModule({
      providers: [
        IncomeSourcesStore,
        {provide: IncomeSourcesService, useValue: service},
        {provide: ERROR_MESSAGES, useValue: ERROR_MESSAGES_REGISTRY},
      ],
    });
  });

  it('loads on init and reads as loading until the answer arrives', () => {
    service.list.mockReturnValue(NEVER);

    const store = TestBed.inject(IncomeSourcesStore);

    expect(store.isLoading()).toBe(true);
  });

  it('stores the sources and settles on success', () => {
    const store = TestBed.inject(IncomeSourcesStore);

    expect(store.sources()).toEqual([MONTHLY_SALARY]);
    expect(store.sourcesStatus()).toBe('success');
    expect(store.rows()).toHaveLength(1);
  });

  it('reports a failed load with the flow fallback', () => {
    service.list.mockReturnValue(apiError('UNKNOWN'));

    const store = TestBed.inject(IncomeSourcesStore);

    expect(store.sourcesStatus()).toBe('error');
    expect(store.sourcesErrorMessage()).toBe('Failed to load income sources.');
  });

  it('creates a new source and reloads', () => {
    const store = TestBed.inject(IncomeSourcesStore);
    service.list.mockClear();

    store.save({id: null, input: INPUT});

    expect(service.create).toHaveBeenCalledWith(INPUT);
    expect(service.list).toHaveBeenCalledTimes(1);
    expect(store.saveStatus()).toBe('success');
  });

  it('updates an existing source', () => {
    const store = TestBed.inject(IncomeSourcesStore);

    store.save({id: 's-1', input: INPUT});

    expect(service.update).toHaveBeenCalledWith('s-1', INPUT);
  });

  it('shows a save error from the API', () => {
    service.create.mockReturnValue(apiError('INCOME_SOURCE_NOT_FOUND'));
    const store = TestBed.inject(IncomeSourcesStore);

    store.save({id: null, input: INPUT});

    expect(store.saveStatus()).toBe('error');
    expect(store.saveErrorMessage()).toBe(ERROR_MESSAGES_REGISTRY['INCOME_SOURCE_NOT_FOUND']);
  });

  it('retires a source and reloads', () => {
    const store = TestBed.inject(IncomeSourcesStore);
    service.list.mockClear();

    store.retire('s-1');

    expect(service.retire).toHaveBeenCalledWith('s-1');
    expect(service.list).toHaveBeenCalledTimes(1);
    expect(store.busyId()).toBeNull();
  });

  it('deletes a source and reloads', () => {
    const store = TestBed.inject(IncomeSourcesStore);
    service.list.mockClear();

    store.remove('s-1');

    expect(service.remove).toHaveBeenCalledWith('s-1');
    expect(service.list).toHaveBeenCalledTimes(1);
  });

  it('surfaces a failed retire as an action error', () => {
    service.retire.mockReturnValue(apiError('INCOME_SOURCE_NOT_FOUND'));
    const store = TestBed.inject(IncomeSourcesStore);

    store.retire('s-1');

    expect(store.busyId()).toBeNull();
    expect(store.actionErrorMessage()).toBe(ERROR_MESSAGES_REGISTRY['INCOME_SOURCE_NOT_FOUND']);
  });

  it('surfaces a failed delete as an action error', () => {
    service.remove.mockReturnValue(apiError('INCOME_SOURCE_NOT_FOUND'));
    const store = TestBed.inject(IncomeSourcesStore);

    store.remove('s-1');

    expect(store.actionErrorMessage()).toBe(ERROR_MESSAGES_REGISTRY['INCOME_SOURCE_NOT_FOUND']);
  });
});
