import {expect} from '@playwright/test';

import {mockGet, type VisualScreen} from '../visual-baseline';

export const SETTINGS_SCREEN: VisualScreen = {
  name: 'settings',
  title: 'Settings',
  path: '/settings',
  desktopHeight: 2400,
  phoneHeight: 3300,
  mocks: async page => {
    await mockGet(page, 'notifications/push/public-key', {available: false, publicKey: null});
    await mockGet(page, 'notifications/push/subscriptions', []);
    await mockGet(page, 'notifications/push/preferences', {pushEnabled: false});
    await mockGet(page, 'account/closure', {canClose: true, reason: null, graceDays: 30});
    await mockGet(page, 'wealth/income-sources', [
      {
        id: 'a1',
        kind: 'Salary',
        label: 'Day job',
        amount: 600,
        currency: 'EUR',
        cadence: 'Weekly',
        grossOrNet: 'Net',
        startDate: '2025-01-01',
        endDate: null,
        monthlyEquivalent: 2600,
        active: true,
        updatedAt: '2026-09-01T10:00:00Z',
      },
    ]);
    await mockGet(page, 'wealth/career-events', [
      {
        id: 'c1',
        kind: 'IncomeStep',
        date: '2099-03-01',
        endDate: null,
        label: 'Promotion',
        amount: 500,
        currency: 'EUR',
        note: null,
        counted: true,
        retired: false,
        updatedAt: '2026-09-01T10:00:00Z',
      },
    ]);
  },
  ready: async page => {
    await expect(page.getByRole('heading', {name: 'Appearance'})).toBeVisible();
    await expect(page.getByRole('heading', {name: 'Security'})).toBeVisible();
  },
};
