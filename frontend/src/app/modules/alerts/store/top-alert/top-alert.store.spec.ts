import {HttpErrorResponse} from '@angular/common/http';
import {TestBed} from '@angular/core/testing';
import {of, throwError} from 'rxjs';
import {describe, expect, it, vi} from 'vitest';

import {type Alert, type AlertsPageResponse} from '../../models/alert/alert.model';
import {AlertsService} from '../../services/alerts.service';
import {TopAlertStore} from './top-alert.store';

const HTTP_SERVER_ERROR = 500;

const ALERT: Alert = {
  id: 'a',
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
};

const page = (items: Alert[], totalCount: number): AlertsPageResponse => ({
  items,
  totalCount,
  unreadCount: totalCount,
  page: 1,
  pageSize: 1,
  totalPages: totalCount,
});

function create(getAlerts: AlertsService['getAlerts']) {
  TestBed.configureTestingModule({
    providers: [TopAlertStore, {provide: AlertsService, useValue: {getAlerts}}],
  });
  return TestBed.inject(TopAlertStore);
}

describe('TopAlertStore', () => {
  it('asks for one row of the unread alerts, the newest', () => {
    const getAlerts = vi.fn(() => of(page([ALERT], 1)));
    create(getAlerts);

    expect(getAlerts).toHaveBeenCalledExactlyOnceWith('unread', 1, 1);
  });

  it('holds the newest unread alert and counts the rest', () => {
    const store = create(() => of(page([ALERT], 3)));

    expect(store.alert()).toEqual(ALERT);
    expect(store.moreCount()).toBe(2);
    expect(store.status()).toBe('success');
  });

  it('holds nothing when every alert is read', () => {
    const store = create(() => of(page([], 0)));

    expect(store.alert()).toBeNull();
    expect(store.moreCount()).toBe(0);
  });

  it('holds nothing and records the failure when Alerts cannot be reached', () => {
    const store = create(() =>
      throwError(() => new HttpErrorResponse({status: HTTP_SERVER_ERROR}))
    );

    expect(store.alert()).toBeNull();
    expect(store.status()).toBe('error');
  });
});
