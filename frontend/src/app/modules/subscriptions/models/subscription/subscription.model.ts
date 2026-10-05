export type SubscriptionStatus = 'active' | 'dismissed' | 'potentially_cancelled' | 'completed';
export type SubscriptionSort = 'date' | 'amount' | 'name';
export type DismissedSubscription = Extract<SubscriptionStatus, 'dismissed'>;
export type SubscriptionKind = 'subscription' | 'installment';

export interface Subscription {
  id: string;
  merchantName: string;
  cadence: 'monthly' | 'annual';
  averageAmount: number;
  lastKnownAmount: number;
  monthlyEquivalent: number;
  currency: string;
  lastChargeDate: string;
  nextExpectedDate: string;
  status: SubscriptionStatus;
  occurrenceCount: number;
  kind: SubscriptionKind;
  termCount: Nullable<number>;
  endDate: Nullable<string>;
  /** User-set plan start; detection only sees ~13 months, so an older plan needs this. */
  startDate: Nullable<string>;
  remainingPayments: Nullable<number>;
  isManual: boolean;
}

/** Adds a commitment from a picked transaction; later charges like it keep it current. */
export interface AddCommitmentRequest {
  transactionId: string;
  kind: SubscriptionKind;
  merchant: string;
  monthlyAmount: number;
  termCount: Nullable<number>;
}

export interface SpendBucket {
  /** Current monthly run-rate. */
  monthly: number;
  /**
   * What actually leaves the account over the next 12 months. Subscriptions are open-ended
   * (`monthly × 12`); an installment contributes only its remaining payments, capped at 12.
   */
  next12Months: number;
  /** Total still owed until every plan ends — null for open-ended buckets. */
  remainingCommitment: Nullable<number>;
  activeCount: number;
  /** A plan in this bucket has no term or end date, so the figures assume it continues. */
  hasUnknownSchedule: boolean;
}

export interface SubscriptionSummary {
  subscriptions: SpendBucket;
  installments: SpendBucket;
  combined: SpendBucket;
  potentiallyCancelledCount: number;
  currency: string;
}

export interface SubscriptionsListResponse {
  items: Subscription[];
  totalCount: number;
  hasInsufficientHistory: boolean;
}

export interface SubscriptionSection {
  /** `missed`: the expected charge date has passed without a matching charge. */
  id: 'due' | 'missed' | 'later';
  label: Nullable<string>;
  items: Subscription[];
}
