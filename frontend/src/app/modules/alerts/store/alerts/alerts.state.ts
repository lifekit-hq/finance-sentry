import {type Alert, type AlertFilter} from '../../models/alert/alert.model';

/** Alerts per page; the list asks for the next page as its end comes into reach. */
export const ALERTS_PAGE_SIZE = 20;

export interface AlertsState {
  alerts: Alert[];
  filter: AlertFilter;
  search: string;
  totalCount: number;
  unreadCount: number;
  /** The last page asked for added no row: the server holds nothing more to show. */
  exhausted: boolean;
}

export const initialAlertsState: AlertsState = {
  alerts: [],
  filter: 'all',
  search: '',
  totalCount: 0,
  unreadCount: 0,
  exhausted: false,
};
