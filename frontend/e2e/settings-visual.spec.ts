import {expect} from '@playwright/test';

import {describeVisualBaseline, mockGet} from './support/visual-baseline';

describeVisualBaseline({
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
  },
  ready: async page => {
    await expect(page.getByRole('heading', {name: 'Appearance'})).toBeVisible();
    await expect(page.getByRole('heading', {name: 'Security'})).toBeVisible();
  },
});
