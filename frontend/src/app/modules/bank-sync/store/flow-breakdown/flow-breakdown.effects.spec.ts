import {provideHttpClient, withXhr} from '@angular/common/http';
import {HttpTestingController, provideHttpClientTesting} from '@angular/common/http/testing';
import {TestBed} from '@angular/core/testing';
import {ActivatedRoute, convertToParamMap} from '@angular/router';
import {provideApiBaseUrl} from '@lifekit-hq/core';
import {BehaviorSubject} from 'rxjs';
import {afterEach, beforeEach, describe, expect, it, vi} from 'vitest';

import {FlowBreakdownStore} from './flow-breakdown.store';

const NOW = new Date('2026-08-12T10:00:00.000Z');
const URL_PATH = '/dashboard/flow-breakdown';

function setup(query: Record<string, string>) {
  TestBed.configureTestingModule({
    providers: [
      FlowBreakdownStore,
      provideHttpClient(withXhr()),
      provideHttpClientTesting(),
      provideApiBaseUrl('http://localhost/api/v1'),
      {
        provide: ActivatedRoute,
        useValue: {queryParamMap: new BehaviorSubject(convertToParamMap(query))},
      },
    ],
  });
  const store = TestBed.inject(FlowBreakdownStore);
  const http = TestBed.inject(HttpTestingController);
  return {store, http};
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
  });

  it('loads a month from the month param', () => {
    const {http} = setup({month: '2026-05'});

    expect(http.expectOne(r => r.url.endsWith(URL_PATH)).request.params.get('month')).toBe(
      '2026-05'
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
      kind: 'range',
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
});
