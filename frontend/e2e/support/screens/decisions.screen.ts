import {expect} from '@playwright/test';

import {mockRecommendations} from '../recommendation-mocks';
import type {VisualScreen} from '../visual-baseline';

export const DECISIONS_SCREEN: VisualScreen = {
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
};
