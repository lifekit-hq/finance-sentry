import {type AlertFilter} from '../../models/alert/alert.model';

export interface AlertFilterOption {
  value: AlertFilter;
  label: string;
}

/** Read-state filters; `all` doubles as the sheet's "no filter" choice. */
export const ALERT_READ_STATE_FILTERS: AlertFilterOption[] = [
  {value: 'all', label: 'All'},
  {value: 'unread', label: 'Unread'},
];

export const ALERT_SEVERITY_FILTERS: AlertFilterOption[] = [
  {value: 'error', label: 'Errors'},
  {value: 'warning', label: 'Warnings'},
  {value: 'info', label: 'Info'},
];

export const ALERT_FILTER_LABELS: Record<AlertFilter, string> = {
  all: 'All',
  unread: 'Unread',
  error: 'Errors',
  warning: 'Warnings',
  info: 'Info',
};
