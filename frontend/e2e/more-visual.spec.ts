import {expect} from '@playwright/test';

import {describeVisualBaseline} from './support/visual-baseline';

describeVisualBaseline({
  name: 'more',
  title: 'More',
  path: '/more',
  mocks: () => Promise.resolve(),
  ready: async page => {
    await expect(page.getByTestId('more-version')).toBeVisible();
  },
});
