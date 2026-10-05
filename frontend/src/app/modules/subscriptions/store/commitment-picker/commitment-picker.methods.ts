import {patchState, type WritableStateSource} from '@ngrx/signals';

import {
  type CommitmentAnchor,
  type CommitmentCandidate,
} from '../../models/commitment-candidate/commitment-candidate.model';
import {type CommitmentPickerState} from './commitment-picker.state';

export function commitmentPickerMethods(store: WritableStateSource<CommitmentPickerState>) {
  return {
    setSearch(search: string): void {
      patchState(store, {search});
    },
    setLoading(): void {
      patchState(store, {status: 'loading', errorCode: null});
    },
    setCandidates(candidates: CommitmentCandidate[]): void {
      patchState(store, {candidates, status: 'idle'});
    },
    setError(errorCode: Nullable<string>): void {
      patchState(store, {status: 'error', errorCode});
    },
    select(candidate: CommitmentCandidate): void {
      patchState(store, {selected: candidate, anchor: null});
    },
    setAnchor(anchor: CommitmentAnchor): void {
      patchState(store, {anchor});
    },
  };
}
