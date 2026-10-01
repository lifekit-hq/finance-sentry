import {patchState, type WritableStateSource} from '@ngrx/signals';

import {
  type DashboardData,
  type HistoryRange,
  type NetWorthSnapshotDto,
} from '../../models/dashboard/dashboard.model';
import {type DashboardState} from './dashboard.state';

export function dashboardMethods(store: WritableStateSource<DashboardState>) {
  return {
    setData(data: DashboardData): void {
      patchState(store, {data});
    },
    setNetWorthHistory(snapshots: NetWorthSnapshotDto[]): void {
      patchState(store, {netWorthHistory: snapshots});
    },
    setHistoryRange(range: HistoryRange): void {
      patchState(store, {historyRange: range});
    },
    setHistoryLoading(loading: boolean): void {
      patchState(store, {historyLoading: loading});
    },
    setHistoryError(error: string | null): void {
      patchState(store, {historyError: error});
    },
    setHistoryHasHistory(hasHistory: boolean): void {
      patchState(store, {historyHasHistory: hasHistory});
    },
  };
}
