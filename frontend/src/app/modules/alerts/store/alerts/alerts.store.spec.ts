import {TestBed} from '@angular/core/testing';
import {of} from 'rxjs';
import {beforeEach, describe, expect, it} from 'vitest';

import {type Alert, type AlertFilter} from '../../models/alert/alert.model';
import {AlertsService} from '../../services/alerts.service';
import {ALERTS_PAGE_SIZE} from './alerts.state';
import {AlertsStore} from './alerts.store';

const MAX_SCROLLS = 10;

function makeServer(count: number) {
  let rows: Alert[] = Array.from(
    {length: count},
    (_, i) => ({id: `a${i}`, isRead: false, isResolved: false}) as Alert
  );
  const visible = (filter: AlertFilter) => rows.filter(a => filter !== 'unread' || !a.isRead);
  return {
    ids: (filter: AlertFilter) => visible(filter).map(a => a.id),
    service: {
      getAlerts: (filter: AlertFilter, page: number, pageSize: number) => {
        const all = visible(filter);
        return of({
          items: all.slice((page - 1) * pageSize, page * pageSize),
          totalCount: all.length,
          unreadCount: rows.filter(a => !a.isRead).length,
          page,
          pageSize,
          totalPages: Math.ceil(all.length / pageSize),
        });
      },
      getUnreadCount: () => of({count: rows.filter(a => !a.isRead).length}),
      markRead: (id: string) => {
        rows = rows.map(a => (a.id === id ? {...a, isRead: true} : a));
        return of(undefined);
      },
      dismiss: (id: string) => {
        rows = rows.filter(a => a.id !== id);
        return of(undefined);
      },
    },
  };
}

function open(server: ReturnType<typeof makeServer>, filter: AlertFilter) {
  TestBed.configureTestingModule({providers: [{provide: AlertsService, useValue: server.service}]});
  const store = TestBed.inject(AlertsStore);
  store.setFilter(filter);
  TestBed.tick();
  return store;
}

function scrollToEnd(store: InstanceType<typeof AlertsStore>): void {
  for (let i = 0; i < MAX_SCROLLS && store.hasMore(); i++) {
    store.loadMore();
  }
}

describe('AlertsStore continuous paging', () => {
  beforeEach(() => TestBed.resetTestingModule());

  it('loads every server row once after a dismiss between pages (All filter)', () => {
    const server = makeServer(2 * ALERTS_PAGE_SIZE);
    const store = open(server, 'all');
    expect(store.alerts()).toHaveLength(ALERTS_PAGE_SIZE);

    store.dismiss('a3');
    scrollToEnd(store);

    const ids = store.alerts().map(a => a.id);
    expect(ids).toEqual(server.ids('all'));
    expect(new Set(ids).size).toBe(ids.length);
    expect(store.hasMore()).toBe(false);
  });

  it('loads every server row once after rows are marked read between pages (Unread filter)', () => {
    const server = makeServer(37);
    const store = open(server, 'unread');
    expect(store.alerts()).toHaveLength(ALERTS_PAGE_SIZE);

    store.markRead('a0');
    store.markRead('a1');
    store.markRead('a2');
    scrollToEnd(store);

    const ids = store.alerts().map(a => a.id);
    expect(ids).toEqual(server.ids('unread'));
    expect(new Set(ids).size).toBe(ids.length);
    expect(store.hasMore()).toBe(false);
  });
});
