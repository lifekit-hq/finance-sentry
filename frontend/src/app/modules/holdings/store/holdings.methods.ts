import {type Nullable} from '@lifekit-hq/ui';
import {patchState, type WritableStateSource} from '@ngrx/signals';

import {type Headline} from '../../../shared/models/headline/headline.model';
import {type VerdictWord} from '../../../shared/models/scorecard/scorecard.model';
import {type Position} from '../models/position/position.model';
import {type HoldingsState} from './holdings.state';

export function holdingsMethods(store: WritableStateSource<HoldingsState>) {
  return {
    setPositions(positions: Position[]): void {
      patchState(store, {positions});
    },
    setHeadline(headline: Nullable<Headline>): void {
      patchState(store, {headline});
    },
    setDayChangePct(dayChangePctByTicker: Record<string, number>): void {
      patchState(store, {dayChangePctByTicker});
    },
    setVerdicts(verdictByTicker: Record<string, VerdictWord>): void {
      patchState(store, {verdictByTicker});
    },
  };
}
