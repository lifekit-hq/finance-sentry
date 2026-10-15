import {type Alert, type AlertFilter} from '../../models/alert/alert.model';

/** Alerts per page; the list asks for the next page as its end comes into reach. */
export const ALERTS_PAGE_SIZE = 20;

export interface AlertsState {
  alerts: Alert[];
  filter: AlertFilter;
  search: string;
  totalCount: number;
  unreadCount: number;
  /** Pages loaded so far: the next page appends `currentPage + 1`. */
  currentPage: number;
}

export const initialAlertsState: AlertsState = {
  alerts: [],
  filter: 'all',
  search: '',
  totalCount: 0,
  unreadCount: 0,
  currentPage: 1,
};
