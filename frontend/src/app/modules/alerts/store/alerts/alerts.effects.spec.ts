import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {of, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {type AlertsQuery} from '../../models/alert/alerts-query.model';
import {AlertsService} from '../../services/alerts.service';
import {alertsEffects, alertsHooks} from './alerts.effects';

const PAGE = {items: [], totalCount: 45, unreadCount: 7, page: 2, pageSize: 20, totalPages: 3};

function build() {
  return {
    setData: vi.fn(),
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

  it('load fetches the requested filter, page and page size', () => {
    const store = build();
    TestBed.runInInjectionContext(() => {
      alertsEffects(store).load({filter: 'error', page: 2, pageSize: 50});
    });

    expect(api.getAlerts).toHaveBeenCalledWith('error', 2, 50);
    expect(store.setLoading).toHaveBeenCalledOnce();
    expect(store.setData).toHaveBeenCalledWith([], 45, 7);
    expect(store.setSuccess).toHaveBeenCalledOnce();
    expect(store.setError).not.toHaveBeenCalled();
  });

  it('load records an error and no data when the request fails', () => {
    api.getAlerts.mockReturnValue(throwError(() => new Error('boom')));
    const store = build();
    TestBed.runInInjectionContext(() => {
      alertsEffects(store).load({filter: 'all', page: 1, pageSize: 20});
    });

    expect(store.setError).toHaveBeenCalledWith(null);
    expect(store.setData).not.toHaveBeenCalled();
    expect(store.setSuccess).not.toHaveBeenCalled();
  });

  it('the hook refetches whenever the query signal changes', () => {
    const store = build();
    const query = signal<AlertsQuery>({filter: 'all', page: 1, pageSize: 20});
    TestBed.runInInjectionContext(() => {
      const effects = alertsEffects(store);
      alertsHooks({query, load: effects.load, loadUnreadCount: effects.loadUnreadCount});
    });
    TestBed.tick();
    expect(api.getAlerts).toHaveBeenLastCalledWith('all', 1, 20);

    query.set({filter: 'all', page: 2, pageSize: 20});
    TestBed.tick();
    expect(api.getAlerts).toHaveBeenLastCalledWith('all', 2, 20);
    expect(api.getAlerts).toHaveBeenCalledTimes(2);
  });
});
