import {type Alert, type AlertFilter} from '../../models/alert/alert.model';

const DEFAULT_PAGE_SIZE = 20;

export interface AlertsState {
  alerts: Alert[];
  filter: AlertFilter;
  search: string;
  totalCount: number;
  unreadCount: number;
  currentPage: number;
  pageSize: number;
}

export const initialAlertsState: AlertsState = {
  alerts: [],
  filter: 'all',
  search: '',
  totalCount: 0,
  unreadCount: 0,
  currentPage: 1,
  pageSize: DEFAULT_PAGE_SIZE,
};
