import {expect} from '@playwright/test';

import {describeVisualBaseline, mockGet} from './support/visual-baseline';

const CONVERSATIONS = [
  {
    id: 'conv-1',
    title: 'Allocation drift this quarter',
    updatedAt: '2026-09-14T18:00:00Z',
    modelId: 'claude-sonnet-5-5',
  },
  {
    id: 'conv-2',
    title: 'Is the services thesis intact?',
    updatedAt: '2026-09-10T09:00:00Z',
    modelId: 'claude-sonnet-5-5',
  },
];

describeVisualBaseline({
  name: 'ledger',
  title: 'Ledger chat',
  path: '/ledger',
  mocks: async page => {
    await mockGet(page, 'agent/conversations', CONVERSATIONS);
  },
  ready: async page => {
    // On a phone the list sits in a drawer behind the Sessions button, so it is attached but hidden.
    await expect(page.locator('aside[aria-label="Conversations"]')).toContainText(
      'Allocation drift this quarter'
    );
  },
});
