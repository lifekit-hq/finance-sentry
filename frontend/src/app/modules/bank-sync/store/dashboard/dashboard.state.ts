import {
  type DashboardData,
  type HistoryRange,
  type NetWorthSnapshotDto,
} from '../../models/dashboard/dashboard.model';

export interface DashboardState {
  data: Nullable<DashboardData>;
  netWorthHistory: NetWorthSnapshotDto[];
  historyRange: HistoryRange;
  historyHasHistory: boolean;
  historyLoading: boolean;
  historyError: string | null;
}

export const initialDashboardState: DashboardState = {
  data: null,
  netWorthHistory: [],
  historyRange: '3m',
  historyHasHistory: false,
  historyLoading: false,
  historyError: null,
};
