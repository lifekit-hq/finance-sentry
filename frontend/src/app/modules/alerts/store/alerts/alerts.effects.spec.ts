import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {type AsyncStatus} from '@lifekit-hq/core';
import {of, Subject, throwError} from 'rxjs';
import {afterEach, beforeEach, describe, expect, it, vi} from 'vitest';

import {type Alert, type AlertFilter} from '../../models/alert/alert.model';
import {type AlertsQuery} from '../../models/alert/alerts-query.model';
import {AlertsService} from '../../services/alerts.service';
import {ALERT_SEARCH_DEBOUNCE_MS, alertsEffects, alertsHooks} from './alerts.effects';
import {ALERTS_PAGE_SIZE} from './alerts.state';

const PAGE = {items: [], totalCount: 45, unreadCount: 7, page: 2, pageSize: 20, totalPages: 3};

function build() {
  return {
    search: signal(''),
    filter: signal<AlertFilter>('all'),
    status: signal<AsyncStatus>('idle'),
    alerts: signal<Alert[]>([]),
    setSearch: vi.fn(),
    setData: vi.fn(),
    appendData: vi.fn(),
    clearData: vi.fn(),
    setAppendLoading: vi.fn(),
    setAppendSuccess: vi.fn(),
    setAppendError: vi.fn(),
    setAppendIdle: vi.fn(),
    setUnreadCount: vi.fn(),
    setLoading: vi.fn(),
    setSuccess: vi.fn(),
    setError: vi.fn(),
    markReadLocal: vi.fn(),
    markAllReadLocal: vi.fn(),
    dismissLocal: vi.fn(),
  };
}

describe('alertsEffects', () => {
  const api = {
    getAlerts: vi.fn(),
    getUnreadCount: vi.fn(),
  };

  beforeEach(() => {
    api.getAlerts.mockReset().mockReturnValue(of(PAGE));
    api.getUnreadCount.mockReset().mockReturnValue(of({count: 7}));
    TestBed.configureTestingModule({providers: [{provide: AlertsService, useValue: api}]});
  });

  it('load fetches the first page of the requested filter and search', () => {
    const store = build();
    TestBed.runInInjectionContext(() => {
      alertsEffects(store).load({filter: 'error', search: 'rent'});
    });

    expect(api.getAlerts).toHaveBeenCalledWith('error', 1, ALERTS_PAGE_SIZE, 'rent');
    expect(store.setLoading).toHaveBeenCalledOnce();
    expect(store.setData).toHaveBeenCalledWith([], 45, 7);
    expect(store.setSuccess).toHaveBeenCalledOnce();
    expect(store.setError).not.toHaveBeenCalled();
  });

  it('load records an error and no data when the request fails', () => {
    api.getAlerts.mockReturnValue(throwError(() => new Error('boom')));
    const store = build();
    TestBed.runInInjectionContext(() => {
      alertsEffects(store).load({filter: 'all', search: ''});
    });

    expect(store.setError).toHaveBeenCalledWith(null);
    expect(store.clearData).toHaveBeenCalled();
    expect(store.setData).not.toHaveBeenCalled();
    expect(store.setSuccess).not.toHaveBeenCalled();
  });

  it('the hook refetches whenever the query signal changes', () => {
    const store = build();
    const query = signal<AlertsQuery>({filter: 'all', search: ''});
    TestBed.runInInjectionContext(() => {
      const effects = alertsEffects(store);
      alertsHooks({query, load: effects.load, loadUnreadCount: effects.loadUnreadCount});
    });
    TestBed.tick();
    expect(api.getAlerts).toHaveBeenLastCalledWith('all', 1, ALERTS_PAGE_SIZE, '');

    query.set({filter: 'unread', search: ''});
    TestBed.tick();
    expect(api.getAlerts).toHaveBeenLastCalledWith('unread', 1, ALERTS_PAGE_SIZE, '');
    expect(api.getAlerts).toHaveBeenCalledTimes(2);
  });

  it('loadMore appends the page after the loaded ones under its own status', () => {
    const store = build();
    store.filter.set('error');
    store.alerts.set(Array.from({length: 2 * ALERTS_PAGE_SIZE}, (_, i) => ({id: `a${i}`}) as Alert));
    TestBed.runInInjectionContext(() => alertsEffects(store).loadMore());

    expect(api.getAlerts).toHaveBeenCalledWith('error', 3, ALERTS_PAGE_SIZE, '');
    expect(store.setAppendLoading).toHaveBeenCalledOnce();
    expect(store.appendData).toHaveBeenCalledWith([], 45, 7);
    expect(store.setAppendSuccess).toHaveBeenCalledOnce();
    expect(store.setLoading).not.toHaveBeenCalled();
  });

  it('loadMore waits while the first page is loading', () => {
    const store = build();
    store.status.set('loading');
    TestBed.runInInjectionContext(() => alertsEffects(store).loadMore());

    expect(api.getAlerts).not.toHaveBeenCalled();
    expect(store.setAppendLoading).not.toHaveBeenCalled();
  });

  it('a new first page drops the next page still in flight', () => {
    const stalePage = new Subject<unknown>();
    api.getAlerts.mockReturnValueOnce(stalePage).mockReturnValueOnce(of(PAGE));
    const store = build();
    TestBed.runInInjectionContext(() => {
      const effects = alertsEffects(store);
      effects.loadMore();
      effects.load({filter: 'error', search: ''});
    });
    stalePage.next(PAGE);

    expect(store.appendData).not.toHaveBeenCalled();
    expect(store.setData).toHaveBeenCalledOnce();
    expect(store.setAppendIdle).toHaveBeenCalledOnce();
  });

  it('loadMore keeps the rows and flags only the append on failure', () => {
    api.getAlerts.mockReturnValue(throwError(() => new Error('boom')));
    const store = build();
    TestBed.runInInjectionContext(() => alertsEffects(store).loadMore());

    expect(store.setAppendError).toHaveBeenCalledWith(null);
    expect(store.setError).not.toHaveBeenCalled();
    expect(store.clearData).not.toHaveBeenCalled();
  });
});

describe('alertsEffects.applySearch', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({providers: [{provide: AlertsService, useValue: {}}]});
  });

  afterEach(() => vi.useRealTimers());

  it('writes the box to the store once typing pauses', () => {
    const store = build();
    const box = signal('');
    TestBed.runInInjectionContext(() => alertsEffects(store).applySearch(box));
    TestBed.tick();

    box.set('re');
    TestBed.tick();
    box.set('rent');
    TestBed.tick();
    vi.advanceTimersByTime(ALERT_SEARCH_DEBOUNCE_MS);

    expect(store.setSearch).toHaveBeenCalledExactlyOnceWith('rent');
  });

  it('ignores a box that only differs from the store by whitespace', () => {
    const store = build();
    store.search.set('rent');
    const box = signal('rent');
    TestBed.runInInjectionContext(() => alertsEffects(store).applySearch(box));
    TestBed.tick();
    box.set('rent ');
    TestBed.tick();
    vi.advanceTimersByTime(ALERT_SEARCH_DEBOUNCE_MS);

    expect(store.setSearch).not.toHaveBeenCalled();
  });
});
