import {expect} from '@playwright/test';

import {mockAccounts} from './support/accounts-mocks';
import {describeVisualBaseline} from './support/visual-baseline';

describeVisualBaseline({
  name: 'accounts',
  title: 'Accounts list',
  path: '/accounts/list',
  desktopHeight: 1400,
  mocks: mockAccounts,
  ready: async page => {
    await expect(page.getByTestId('accounts-net-worth')).toContainText('$53,700.00');
    await expect(page.locator('cmn-disclosure-row').first()).toBeVisible();
  },
});
