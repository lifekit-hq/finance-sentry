import {type AlertFilter} from '@shared/models/alert/alert.model';

export interface AlertsQuery {
  filter: AlertFilter;
  /** Free text matched against an alert's title, message and reference; '' matches all. */
  search: string;
}
