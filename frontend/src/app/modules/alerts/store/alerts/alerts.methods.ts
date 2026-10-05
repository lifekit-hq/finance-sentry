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
      patchState(store, {alerts, totalCount, unreadCount, status: 'idle'});
    },
    setUnreadCount(unreadCount: number): void {
      patchState(store, {unreadCount});
    },
    setStatus(status: AsyncStatus): void {
      patchState(store, {status});
    },
    setFilter(filter: AlertFilter): void {
      patchState(store, {filter, currentPage: 1});
    },
    setPage(currentPage: number): void {
      patchState(store, {currentPage: Math.max(1, currentPage)});
    },
    setPageSize(pageSize: number): void {
      patchState(store, {pageSize, currentPage: 1});
    },
    markReadLocal(id: string): void {
      patchState(store, (s: AlertsState) => ({
        alerts: s.alerts.map(a => (a.id === id ? {...a, isRead: true} : a)),
        unreadCount: Math.max(0, s.unreadCount - unreadWeight(s.alerts.find(a => a.id === id))),
      }));
    },
    markAllReadLocal(): void {
      patchState(store, (s: AlertsState) => ({
        alerts: s.alerts.map(a => ({...a, isRead: true})),
        unreadCount: 0,
      }));
    },
    dismissLocal(id: string): void {
      patchState(store, (s: AlertsState) => {
        const alerts = s.alerts.filter(a => a.id !== id);
        return {
          alerts,
          totalCount: Math.max(0, s.totalCount - 1),
          unreadCount: Math.max(0, s.unreadCount - unreadWeight(s.alerts.find(a => a.id === id))),
          // Dismissing the last row of a later page steps back so the pager never shows an empty page.
          currentPage: alerts.length === 0 ? Math.max(1, s.currentPage - 1) : s.currentPage,
        };
      });
    },
  };
}
