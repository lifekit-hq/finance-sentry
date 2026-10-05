import {
  type Subscription,
  type SubscriptionSort,
  type SubscriptionSummary,
} from '../../models/subscription/subscription.model';

export interface SubscriptionsState {
  subscriptions: Subscription[];
  sort: SubscriptionSort;
  summary: Nullable<SubscriptionSummary>;
  hasInsufficientHistory: boolean;
  status: AsyncStatus;
  /** Error code from the last failed add; cleared when the next add starts. */
  addErrorCode: Nullable<string>;
}

export const initialSubscriptionsState: SubscriptionsState = {
  subscriptions: [],
  sort: 'date',
  summary: null,
  hasInsufficientHistory: false,
  status: 'idle',
  addErrorCode: null,
};
