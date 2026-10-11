import {expect} from '@playwright/test';

import {describeVisualBaseline, mockGet} from './support/visual-baseline';

function firedAlert(
  id: string,
  type: string,
  severity: string,
  title: string,
  message: string,
  createdAt: string,
  isRead: boolean
): Record<string, unknown> {
  return {
    id,
    type,
    severity,
    title,
    message,
    referenceId: null,
    referenceLabel: null,
    isRead,
    isResolved: false,
    createdAt,
    resolvedAt: null,
    occurrenceCount: 1,
    lastOccurredAt: createdAt,
  };
}

const ALERTS = {
  items: [
    firedAlert(
      'a-1',
      'SyncFailure',
      'Error',
      'Monzo sync failed',
      'The last sync of Monzo failed. Reconnect the account to resume.',
      '2026-09-15T09:10:00Z',
      false
    ),
    firedAlert(
      'a-2',
      'BudgetBreach',
      'Warning',
      'Transport is over budget',
      'You have spent $135 of your $120 Transport budget this month.',
      '2026-09-14T18:00:00Z',
      false
    ),
    firedAlert(
      'a-3',
      'PriceHike',
      'Info',
      'Netflix went up',
      'Netflix now charges $15.99, up from $13.99.',
      '2026-09-12T08:00:00Z',
      true
    ),
    firedAlert(
      'a-4',
      'EarningsAhead',
      'Info',
      'Earnings ahead: AAPL',
      'AAPL reports on 2026-10-15 (estimated).',
      '2026-09-10T06:00:00Z',
      true
    ),
  ],
  totalCount: 4,
  unreadCount: 2,
  page: 1,
  pageSize: 20,
  totalPages: 1,
};

describeVisualBaseline({
  name: 'alerts',
  title: 'Alerts',
  path: '/alerts',
  mocks: async page => {
    await mockGet(page, 'alerts/unread-count', {count: 2});
    await mockGet(page, 'alerts?**', ALERTS);
  },
  ready: async page => {
    await expect(page.getByText('Monzo sync failed').first()).toBeVisible();
  },
});
