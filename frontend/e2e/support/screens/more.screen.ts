import {expect} from '@playwright/test';

import type {VisualScreen} from '../visual-baseline';

export const MORE_SCREEN: VisualScreen = {
  name: 'more',
  title: 'More',
  path: '/more',
  mocks: () => Promise.resolve(),
  ready: async page => {
    await expect(page.locator('[data-more-version]')).toBeVisible();
  },
};
