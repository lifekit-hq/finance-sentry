import {type Position} from '../models/position/position.model';

export interface HoldingsState {
  positions: Position[];
  positionsStatus: AsyncStatus;
  positionsErrorCode: Nullable<string>;
  /** Day change % per ticker (upper-cased). A missing key means no quote; never cleared on refresh. */
  dayChangePctByTicker: Record<string, number>;
}

export const initialHoldingsState: HoldingsState = {
  positions: [],
  positionsStatus: 'idle',
  positionsErrorCode: null,
  dayChangePctByTicker: {},
};
