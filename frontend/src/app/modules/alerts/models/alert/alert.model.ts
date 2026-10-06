export type AlertType =
  | 'LowBalance'
  | 'SyncFailure'
  | 'UnusualSpend'
  | 'ThesisBroken'
  | 'MarketStructure'
  | 'PolicyViolation'
  | 'Opportunity'
  | 'ConsentExpiring'
  | 'JobFailure'
  | 'PerformanceBrief'
  | 'CashShortfall'
  | 'PriceHike'
  | 'DuplicateCharge'
  | 'CategorySpike'
  | 'FxSpread'
  | 'RebalanceProposal'
  | 'CashSweepProposal'
  | 'EarningsAhead'
  | 'FilingLanded'
  | 'NewsCluster'
  | 'BudgetBreach'
  | 'FamilyStatement'
  | 'FireBrief'
  | 'PolicyReview'
  | 'PolicyReviewMissed'
  | 'RelativeUnderperformance';
export type AlertSeverity = 'Error' | 'Warning' | 'Info';
export type AlertFilter = 'all' | 'unread' | 'error' | 'warning' | 'info';

export interface Alert {
  id: string;
  type: AlertType;
  severity: AlertSeverity;
  title: string;
  message: string;
  referenceId: Nullable<string>;
  referenceLabel: Nullable<string>;
  isRead: boolean;
  isResolved: boolean;
  createdAt: string;
  resolvedAt: Nullable<string>;
  /** Times a suppressed repeat bumped this row; 1 for an alert that fired once. */
  occurrenceCount: number;
  lastOccurredAt: string;
}

export interface AlertsPageResponse {
  items: Alert[];
  totalCount: number;
  unreadCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface UnreadCountResponse {
  count: number;
}
