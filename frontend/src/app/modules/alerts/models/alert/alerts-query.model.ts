import {type AlertFilter} from './alert.model';

export interface AlertsQuery {
  filter: AlertFilter;
  page: number;
  pageSize: number;
  /** Free text matched against an alert's title, message and reference; '' matches all. */
  search: string;
}
