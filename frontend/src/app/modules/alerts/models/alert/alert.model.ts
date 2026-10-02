export type AlertType =
  | 'LowBalance'
  | 'SyncFailure'
  | 'UnusualSpend'
  | 'PolicyViolation'
  | 'Opportunity'
  | 'FilingLanded';
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
