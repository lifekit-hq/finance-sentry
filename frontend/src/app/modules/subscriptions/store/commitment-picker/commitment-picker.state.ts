import {
  type CommitmentAnchor,
  type CommitmentCandidate,
} from '../../models/commitment-candidate/commitment-candidate.model';

export interface CommitmentPickerState {
  search: string;
  candidates: CommitmentCandidate[];
  /** Kept apart from `candidates` so a new search does not drop the pick. */
  selected: Nullable<CommitmentCandidate>;
  /** Latest charge under the pick's key; the dialog pre-fills its amount from it. */
  anchor: Nullable<CommitmentAnchor>;
  status: AsyncStatus;
  errorCode: Nullable<string>;
}

export const initialCommitmentPickerState: CommitmentPickerState = {
  search: '',
  candidates: [],
  selected: null,
  anchor: null,
  status: 'idle',
  errorCode: null,
};
