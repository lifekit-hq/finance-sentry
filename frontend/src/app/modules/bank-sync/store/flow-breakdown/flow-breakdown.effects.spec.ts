import {provideHttpClient, withXhr} from '@angular/common/http';
import {HttpTestingController, provideHttpClientTesting} from '@angular/common/http/testing';
import {TestBed} from '@angular/core/testing';
import {ActivatedRoute, convertToParamMap} from '@angular/router';
import {ERROR_MESSAGES, provideApiBaseUrl} from '@lifekit-hq/core';
import {BehaviorSubject} from 'rxjs';
import {afterEach, beforeEach, describe, expect, it, vi} from 'vitest';

import {ERROR_MESSAGES_REGISTRY} from '../../../../core/errors/error-messages.registry';
import {FlowBreakdownStore} from './flow-breakdown.store';

const NOW = new Date('2026-08-12T10:00:00.000Z');
const URL_PATH = '/dashboard/flow-breakdown';

function setup(query: Record<string, string>) {
  const params$ = new BehaviorSubject(convertToParamMap(query));
  TestBed.configureTestingModule({
    providers: [
      FlowBreakdownStore,
      provideHttpClient(withXhr()),
      provideHttpClientTesting(),
      provideApiBaseUrl('http://localhost/api/v1'),
      {provide: ERROR_MESSAGES, useValue: ERROR_MESSAGES_REGISTRY},
      {
        provide: ActivatedRoute,
        useValue: {queryParamMap: params$},
      },
    ],
  });
  const store = TestBed.inject(FlowBreakdownStore);
  const http = TestBed.inject(HttpTestingController);
  TestBed.tick();
  return {store, http, params$};
}

describe('flowBreakdownHooks', () => {
  beforeEach(() => {
    vi.useFakeTimers({toFake: ['Date']});
    vi.setSystemTime(NOW);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('loads the current month when no param is given', () => {
    const {store, http} = setup({});

    const req = http.expectOne(r => r.url.endsWith(URL_PATH));
    expect(req.request.params.get('month')).toBe('2026-08');
    expect(req.request.params.has('from')).toBe(false);
    expect(store.range()).toBeNull();
    expect(store.month()).toBe('2026-08');
  });

  it('loads a month from the month param', () => {
    const {store, http} = setup({month: '2026-05'});

    expect(http.expectOne(r => r.url.endsWith(URL_PATH)).request.params.get('month')).toBe(
      '2026-05'
    );
    expect(store.month()).toBe('2026-05');
    expect(store.range()).toBeNull();
  });

  it('falls back to the current month for a malformed month param', () => {
    const {store, http} = setup({month: 'last-month'});

    expect(http.expectOne(r => r.url.endsWith(URL_PATH)).request.params.get('month')).toBe(
      '2026-08'
    );
    expect(store.month()).toBe('2026-08');
  });

  it('reloads when the query params change, as on back/forward', () => {
    const {store, http, params$} = setup({month: '2026-05'});
    http.expectOne(r => r.url.endsWith(URL_PATH)).flush({month: '2026-05', items: []});

    params$.next(convertToParamMap({month: '2026-04'}));
    TestBed.tick();

    expect(http.expectOne(r => r.url.endsWith(URL_PATH)).request.params.get('month')).toBe(
      '2026-04'
    );
    expect(store.month()).toBe('2026-04');

    params$.next(convertToParamMap({}));
    TestBed.tick();

    expect(http.expectOne(r => r.url.endsWith(URL_PATH)).request.params.get('month')).toBe(
      '2026-08'
    );
  });

  it('loads the dashboard window as a day range with the dashboard history months', () => {
    const {store, http} = setup({from: '2026-08-06', to: '2026-08-12', months: '1'});

    const req = http.expectOne(r => r.url.endsWith(URL_PATH));
    expect(req.request.params.get('from')).toBe('2026-08-06');
    expect(req.request.params.get('to')).toBe('2026-08-12');
    expect(req.request.params.get('months')).toBe('1');
    expect(req.request.params.has('month')).toBe(false);
    expect(store.range()).toEqual({
      from: '2026-08-06',
      to: '2026-08-12',
      months: 1,
    });
  });

  it('sends only the upper bound for an all-time window', () => {
    const {http} = setup({to: '2026-08-12', months: '120'});

    const req = http.expectOne(r => r.url.endsWith(URL_PATH));
    expect(req.request.params.has('from')).toBe(false);
    expect(req.request.params.get('to')).toBe('2026-08-12');
  });

  it('ignores a malformed date and an out-of-range months value', () => {
    const {http} = setup({from: 'yesterday', to: '2026-08-12', months: '9999'});

    const req = http.expectOne(r => r.url.endsWith(URL_PATH));
    expect(req.request.params.has('from')).toBe(false);
    expect(req.request.params.get('months')).toBe('1');
  });

  it('keeps the month mode when from and to are both malformed', () => {
    const {store, http} = setup({from: 'yesterday', to: 'tomorrow'});

    expect(http.expectOne(r => r.url.endsWith(URL_PATH)).request.params.get('month')).toBe(
      '2026-08'
    );
    expect(store.range()).toBeNull();
  });

  it('is loading, then success and empty once an empty breakdown arrives', () => {
    const {store, http} = setup({});
    expect(store.status()).toBe('loading');
    expect(store.isLoading()).toBe(true);
    expect(store.isEmpty()).toBe(false);

    http.expectOne(r => r.url.endsWith(URL_PATH)).flush({month: '2026-08', items: []});

    expect(store.status()).toBe('success');
    expect(store.isLoading()).toBe(false);
    expect(store.isEmpty()).toBe(true);
    expect(store.errorMessage()).toBe('');
  });

  it('shows the default message on a failed load and keeps the stale breakdown', () => {
    const {store, http, params$} = setup({month: '2026-05'});
    const loaded = {month: '2026-05', items: []};
    http.expectOne(r => r.url.endsWith(URL_PATH)).flush(loaded);

    params$.next(convertToParamMap({month: '2026-04'}));
    TestBed.tick();
    http
      .expectOne(r => r.url.endsWith(URL_PATH))
      .flush({errorCode: 'UNKNOWN'}, {status: 500, statusText: 'Server Error'});

    expect(store.status()).toBe('error');
    expect(store.errorMessage()).toBe('Failed to load the month breakdown. Try again.');
    expect(store.breakdown()).toEqual(loaded);
  });
});
