import {expect} from '@playwright/test';

import {mockRecommendations} from './support/recommendation-mocks';
import {describeVisualBaseline} from './support/visual-baseline';

describeVisualBaseline({
  name: 'decisions',
  title: 'Ledger decisions queue',
  path: '/ledger/decisions',
  mocks: mockRecommendations,
  ready: async page => {
    await expect(page.getByTestId('recommendation-card-DRAM')).toBeVisible();
    await expect(page.getByTestId('decisions-decided')).toContainText('NVDA');
  },
  desktopHeight: 1700,
  phoneHeight: 2600,
});
