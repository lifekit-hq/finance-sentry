import {type CommitmentKindOption} from '../../models/commitment-candidate/commitment-candidate.model';

export const CANDIDATE_PAGE_SIZE = 30;
export const CANDIDATE_SEARCH_DEBOUNCE_MS = 300;
export const CANDIDATE_TRANSACTION_TYPE = 'debit';
export const MIN_TERM_COUNT = 1;

export const COMMITMENT_KIND_OPTIONS: CommitmentKindOption[] = [
  {value: 'subscription', label: 'Subscription'},
  {value: 'installment', label: 'Installment'},
];
