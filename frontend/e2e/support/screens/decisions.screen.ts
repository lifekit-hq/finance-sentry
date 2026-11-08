import {expect} from '@playwright/test';

import {mockRecommendations} from '../recommendation-mocks';
import type {VisualScreen} from '../visual-baseline';

export const DECISIONS_SCREEN: VisualScreen = {
  name: 'decisions',
  title: 'Ledger decisions queue',
  path: '/ledger/decisions',
  mocks: mockRecommendations,
  ready: async page => {
    await expect(page.getByTestId('decision-row-DRAM')).toBeVisible();
    await expect(page.getByTestId('decisions-decided')).toContainText('NVDA');
  },
  desktopHeight: 1000,
  phoneHeight: 1100,
};

export const DECISION_DETAIL_SCREEN: VisualScreen = {
  name: 'decision-detail',
  title: 'Ledger decision detail',
  path: '/ledger/decision/rec-dram',
  mocks: mockRecommendations,
  ready: async page => {
    await expect(page.getByTestId('recommendation-card-DRAM')).toBeVisible();
    await expect(page.getByTestId('decision-actions')).toBeVisible();
  },
  desktopHeight: 1000,
  phoneHeight: 1100,
};
