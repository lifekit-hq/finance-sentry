import {DEFAULT_MONTHS_PARAM} from '../../constants/url-params/url-params.constants';
import {type FlowBreakdown} from '../../models/flow-breakdown/flow-breakdown.model';

export interface FlowBreakdownState {
  breakdown: Nullable<FlowBreakdown>;
  /** The `month` query param (`YYYY-MM`); empty when absent or invalid, meaning the current month. */
  selectedMonth: string;
  /** The `from` / `to` query params: a dashboard window's day range, shown instead of a month. */
  from: Nullable<string>;
  to: Nullable<string>;
  /** The `months` query param: the history the dashboard loaded around the window. */
  months: number;
  accountFilter: Nullable<string>;
  status: AsyncStatus;
  errorCode: Nullable<string>;
}

export const initialFlowBreakdownState: FlowBreakdownState = {
  breakdown: null,
  selectedMonth: '',
  from: null,
  to: null,
  months: DEFAULT_MONTHS_PARAM,
  accountFilter: null,
  status: 'idle',
  errorCode: null,
};
