import {patchState, type WritableStateSource} from '@ngrx/signals';

import {
  type RecommendationDto,
  type RecommendationsResult,
} from '../../../../shared/models/recommendation/recommendation.model';
import {type ScorecardDto} from '../../../../shared/models/scorecard/scorecard.model';
import {RecommendationUtils} from '../../utils/recommendation.utils';
import {type DecisionsState} from './decisions.state';

export function decisionsMethods(store: WritableStateSource<DecisionsState>) {
  return {
    setData(result: RecommendationsResult): void {
      patchState(store, {
        pending: RecommendationUtils.oldestFirst(result.pending),
        decided: result.decided,
      });
    },
    /** A recorded verdict leaves the queue and heads the decided list; nothing disappears unrecorded. */
    applyDecision(decided: RecommendationDto): void {
      patchState(store, state => ({
        pending: state.pending.filter(r => r.id !== decided.id),
        decided: [decided, ...state.decided.filter(r => r.id !== decided.id)],
      }));
    },
    setScorecard(card: ScorecardDto): void {
      patchState(store, state => ({scorecards: {...state.scorecards, [card.symbol]: card}}));
    },
  };
}
