import {type AlertFilter} from './alert.model';

export interface AlertsQuery {
  filter: AlertFilter;
  page: number;
  pageSize: number;
}
