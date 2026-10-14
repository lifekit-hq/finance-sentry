import {expect} from '@playwright/test';

import {describeVisualBaseline, mockGet} from './support/visual-baseline';

const PEOPLE = [
  {id: 'u-1', email: 'test@gmail.com', roles: ['Owner'], status: 'Active'},
  {id: 'u-2', email: 'sam@example.com', roles: ['Member'], status: 'Active'},
  {id: 'u-3', email: 'robin@example.com', roles: ['Member'], status: 'Invited'},
  {id: 'u-4', email: 'former@example.com', roles: ['Member'], status: 'Revoked'},
  {
    id: 'u-5',
    email: 'closed@example.com',
    roles: ['Member'],
    status: 'Closed',
    purgeDueAt: '2026-10-10T09:00:00Z',
  },
];

describeVisualBaseline({
  name: 'people',
  title: 'People',
  path: '/settings/people',
  mocks: async page => {
    await mockGet(page, 'people', PEOPLE);
  },
  ready: async page => {
    await expect(page.getByText('sam@example.com').first()).toBeVisible();
  },
});
