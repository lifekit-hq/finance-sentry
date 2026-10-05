import {
  type CommitmentCadenceOption,
  type CommitmentKindOption,
} from '../../models/commitment-candidate/commitment-candidate.model';
import {type SubscriptionCadence} from '../../models/subscription/subscription.model';
import {CADENCE_LABELS} from '../subscription/subscription-form.constants';

export const CANDIDATE_PAGE_SIZE = 30;
export const CANDIDATE_SEARCH_DEBOUNCE_MS = 300;
export const CANDIDATE_TRANSACTION_TYPE = 'debit';
export const MIN_TERM_COUNT = 1;
export const DEFAULT_COMMITMENT_CADENCE: SubscriptionCadence = 'monthly';

export const COMMITMENT_KIND_OPTIONS: CommitmentKindOption[] = [
  {value: 'subscription', label: 'Subscription'},
  {value: 'installment', label: 'Installment'},
];

export const COMMITMENT_CADENCE_OPTIONS: CommitmentCadenceOption[] = [
  {value: 'monthly', label: CADENCE_LABELS.monthly},
  {value: 'annual', label: CADENCE_LABELS.annual},
];
