import {type MaybeHeadline} from '../../../../shared/models/headline/headline.model';
import {DEFAULT_HISTORY_RANGE} from '../../constants/dashboard/dashboard.constants';
import {
  type DashboardData,
  type HistoryRange,
  type NetWorthScopeChangeDto,
  type NetWorthSnapshotDto,
} from '../../models/dashboard/dashboard.model';

export interface DashboardState {
  data: Nullable<DashboardData>;
  /** The range `data` was loaded for; a range change shows skeletons until the new figures land. */
  dataRange: Nullable<HistoryRange>;
  /** Epoch ms of the last successful dashboard load; drives the offline "last synced" notice. */
  lastSyncedAt: Nullable<number>;
  netWorthHistory: NetWorthSnapshotDto[];
  /** The range `netWorthHistory` was loaded for. */
  historyLoadedRange: Nullable<HistoryRange>;
  historyRange: HistoryRange;
  historyHasHistory: boolean;
  /** Accounts linked or removed inside the loaded window; hides the change percentage. */
  historyScopeChanges: NetWorthScopeChangeDto[];
  /** The loaded window's change as a typed record; null when the API build predates it. */
  historyChangeHeadline: MaybeHeadline;
  historyLoading: boolean;
  historyError: string | null;
  /** Index into the drawn history while a pointer scrubs the hero chart; null at rest. */
  scrubIndex: number | null;
}

export const initialDashboardState: DashboardState = {
  data: null,
  dataRange: null,
  lastSyncedAt: null,
  netWorthHistory: [],
  historyLoadedRange: null,
  historyRange: DEFAULT_HISTORY_RANGE,
  historyHasHistory: false,
  historyScopeChanges: [],
  historyChangeHeadline: null,
  historyLoading: false,
  historyError: null,
  scrubIndex: null,
};

/** What a revisit restores: the last visit's numbers and history with the range each was loaded for. */
export const DASHBOARD_SESSION_FIELDS = [
  'data',
  'dataRange',
  'lastSyncedAt',
  'netWorthHistory',
  'historyLoadedRange',
  'historyHasHistory',
  'historyScopeChanges',
  'historyChangeHeadline',
] as const satisfies readonly (keyof DashboardState)[];
