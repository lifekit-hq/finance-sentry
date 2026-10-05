import {type CommitmentCandidate} from '../../models/commitment-candidate/commitment-candidate.model';

export interface CommitmentPickerState {
  search: string;
  candidates: CommitmentCandidate[];
  /** Kept apart from `candidates` so a new search does not drop the pick. */
  selected: Nullable<CommitmentCandidate>;
  status: AsyncStatus;
  errorCode: Nullable<string>;
}

export const initialCommitmentPickerState: CommitmentPickerState = {
  search: '',
  candidates: [],
  selected: null,
  status: 'idle',
  errorCode: null,
};
