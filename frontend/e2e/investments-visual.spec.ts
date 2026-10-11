import {expect} from '@playwright/test';

import {mockAccounts} from './support/accounts-mocks';
import {describeVisualBaseline} from './support/visual-baseline';

describeVisualBaseline({
  name: 'investments',
  title: 'Investments',
  path: '/accounts/investments',
  mocks: mockAccounts,
  ready: async page => {
    await expect(page.getByText('AAPL').first()).toBeVisible();
    await expect(page.getByText('BTC').first()).toBeVisible();
  },
});
