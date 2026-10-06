import {
  type FlowBreakdown,
  type FlowBreakdownRange,
} from '../../models/flow-breakdown/flow-breakdown.model';

export interface FlowBreakdownState {
  breakdown: Nullable<FlowBreakdown>;
  month: string;
  /** Set when the page shows a dashboard window's day range instead of a month. */
  range: Nullable<FlowBreakdownRange>;
  accountFilter: Nullable<string>;
  status: AsyncStatus;
  errorCode: Nullable<string>;
}

export const initialFlowBreakdownState: FlowBreakdownState = {
  breakdown: null,
  month: '',
  range: null,
  accountFilter: null,
  status: 'idle',
  errorCode: null,
};
