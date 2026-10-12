import {patchState, type WritableStateSource} from '@ngrx/signals';

import {type Position} from '../models/position/position.model';
import {type HoldingsState} from './holdings.state';

export function holdingsMethods(store: WritableStateSource<HoldingsState>) {
  return {
    setPositions(positions: Position[]): void {
      patchState(store, {positions});
    },
    setDayChangePct(dayChangePctByTicker: Record<string, number>): void {
      patchState(store, {dayChangePctByTicker});
    },
  };
}
