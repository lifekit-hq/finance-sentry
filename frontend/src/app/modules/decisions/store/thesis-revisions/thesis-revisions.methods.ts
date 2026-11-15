import {patchState, type WritableStateSource} from '@ngrx/signals';

import {type ThesisDto} from '../../../../shared/models/thesis/thesis.model';
import {
  type DecideThesisRevisionResult,
  type ThesisRevisionsResult,
} from '../../../../shared/models/thesis-revision/thesis-revision.model';
import {type ThesisRevisionsState} from './thesis-revisions.state';

export function thesisRevisionsMethods(store: WritableStateSource<ThesisRevisionsState>) {
  return {
    setData(result: ThesisRevisionsResult): void {
      patchState(store, {pending: result.pending, decided: result.decided});
    },
    setTheses(theses: ThesisDto[]): void {
      patchState(store, {theses});
    },
    /** A recorded verdict leaves the queue and heads the decided list; nothing disappears unrecorded. */
    applyDecision(result: DecideThesisRevisionResult): void {
      patchState(store, state => ({
        pending: state.pending.filter(r => r.id !== result.revision.id),
        decided: [result.revision, ...state.decided.filter(r => r.id !== result.revision.id)],
      }));
    },
  };
}
