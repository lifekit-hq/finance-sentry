import {type Nullable} from '@lifekit-hq/ui';

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
  /** Server text of the last failed add, shown only for codes whose text names a row. */
  addErrorDetail: Nullable<string>;
}

export const initialSubscriptionsState: SubscriptionsState = {
  subscriptions: [],
  sort: 'date',
  summary: null,
  hasInsufficientHistory: false,
  addErrorDetail: null,
};
