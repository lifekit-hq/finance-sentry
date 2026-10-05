import {type PagedResponse} from '../../../../shared/models/api/api.model';
import {type SubscriptionCadence, type SubscriptionKind} from '../subscription/subscription.model';

/** A charge the user can pick to start tracking a subscription or installment from. */
export interface CommitmentCandidate {
  transactionId: string;
  bankName: string;
  currency: string;
  amount: number;
  date: string;
  description: string;
  merchantName: Nullable<string>;
}

export type CommitmentCandidatesResponse = PagedResponse<CommitmentCandidate>;

export interface CommitmentKindOption {
  value: SubscriptionKind;
  label: string;
}

export interface CommitmentCadenceOption {
  value: SubscriptionCadence;
  label: string;
}

/** The latest charge under a picked transaction's key: what an added row anchors on. */
export interface CommitmentAnchor {
  amount: number;
  currency: string;
  date: string;
  chargeCount: number;
  cadence: Nullable<SubscriptionCadence>;
}
