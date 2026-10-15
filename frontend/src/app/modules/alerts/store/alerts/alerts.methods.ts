import {patchState, type WritableStateSource} from '@ngrx/signals';

import {type Alert, type AlertFilter} from '../../models/alert/alert.model';
import {type AlertsState} from './alerts.state';

/** The unread badge counts only open, unread alerts — a resolved or already-read row weighs nothing. */
function unreadWeight(alert: Alert | undefined): number {
  return alert && !alert.isRead && !alert.isResolved ? 1 : 0;
}

export function alertsMethods(store: WritableStateSource<AlertsState>) {
  return {
    setData(alerts: Alert[], totalCount: number, unreadCount: number): void {
      patchState(store, {alerts, totalCount, unreadCount, currentPage: 1});
    },
    /** Appends the next page; a row that moved into it since the last page is not shown twice. */
    appendData(alerts: Alert[], totalCount: number, unreadCount: number): void {
      patchState(store, (s: AlertsState) => {
        const loaded = new Set(s.alerts.map(a => a.id));
        return {
          alerts: [...s.alerts, ...alerts.filter(a => !loaded.has(a.id))],
          totalCount,
          unreadCount,
          currentPage: s.currentPage + 1,
        };
      });
    },
    /** A reload whose query failed: its rows no longer match the filter on screen. */
    clearData(): void {
      patchState(store, {alerts: [], totalCount: 0, currentPage: 1});
    },
    setUnreadCount(unreadCount: number): void {
      patchState(store, {unreadCount});
    },
    setFilter(filter: AlertFilter): void {
      patchState(store, {filter, currentPage: 1});
    },
    setSearch(search: string): void {
      patchState(store, {search, currentPage: 1});
    },
    markReadLocal(id: string): void {
      patchState(store, (s: AlertsState) => {
        const target = s.alerts.find(a => a.id === id);
        const unreadCount = Math.max(0, s.unreadCount - unreadWeight(target));
        if (s.filter === 'unread') {
          const alerts = s.alerts.filter(a => a.id !== id);
          return {
            alerts,
            unreadCount,
            totalCount: target && !target.isRead ? Math.max(0, s.totalCount - 1) : s.totalCount,
          };
        }
        return {alerts: s.alerts.map(a => (a.id === id ? {...a, isRead: true} : a)), unreadCount};
      });
    },
    markAllReadLocal(): void {
      patchState(store, (s: AlertsState) =>
        s.filter === 'unread'
          ? {alerts: [], totalCount: 0, unreadCount: 0, currentPage: 1}
          : {alerts: s.alerts.map(a => ({...a, isRead: true})), unreadCount: 0}
      );
    },
    dismissLocal(id: string): void {
      patchState(store, (s: AlertsState) => {
        const alerts = s.alerts.filter(a => a.id !== id);
        return {
          alerts,
          totalCount: Math.max(0, s.totalCount - 1),
          unreadCount: Math.max(0, s.unreadCount - unreadWeight(s.alerts.find(a => a.id === id))),
        };
      });
    },
  };
}
