import {expect} from '@playwright/test';

import {describeVisualBaseline, mockGet} from './support/visual-baseline';

const UPCOMING = {
  items: [
    {
      kind: 'earnings',
      date: '2026-09-15',
      time: null,
      subject: 'MU',
      title: 'Earnings: MU',
      detail: null,
      isEstimate: true,
      source: 'yahoo',
      referenceId: null,
    },
    {
      kind: 'ex_dividend',
      date: '2026-09-15',
      time: null,
      subject: 'PLTR',
      title: 'Ex-dividend: PLTR',
      detail: null,
      isEstimate: false,
      source: 'yahoo',
      referenceId: null,
    },
    {
      kind: 'macro',
      date: '2026-09-18',
      time: '14:00:00',
      subject: 'US',
      title: 'FOMC rate decision',
      detail: 'US · high importance',
      isEstimate: false,
      source: 'https://www.federalreserve.gov',
      referenceId: 'macro-1',
    },
    {
      kind: 'thesis_catalyst',
      date: '2026-09-24',
      time: null,
      subject: 'MU',
      title: 'Thesis catalyst: MU',
      detail: 'HBM4 ramp update',
      isEstimate: false,
      source: 'thesis',
      referenceId: 'thesis-1',
    },
  ],
  from: '2026-09-15',
  to: '2026-10-15',
  sources: [
    {source: 'corporate', status: 'ok'},
    {source: 'macro', status: 'ok'},
    {source: 'theses', status: 'ok'},
    {source: 'filings', status: 'unavailable'},
  ],
};

const FIRED = {
  items: [
    {
      alertId: 'alert-1',
      kind: 'NewsCluster',
      severity: 'Warning',
      subject: 'MU',
      title: 'News cluster: MU',
      message: 'MU news clustered: 3 sources within 2h',
      occurredAt: '2026-09-14T09:35:00Z',
      isRead: false,
      delivery: {
        eventId: 'event-1',
        disposition: 'Delivered',
        dispatchedAt: '2026-09-14T09:36:00Z',
        deliveredAt: '2026-09-14T09:40:00Z',
      },
      verdict: {
        text: 'Two of the three pieces restate the same guidance cut; already priced in.',
        notified: false,
        recordedAt: '2026-09-14T09:41:00Z',
      },
      outcome: 'judged_immaterial',
    },
    {
      alertId: 'alert-2',
      kind: 'EarningsAhead',
      severity: 'Info',
      subject: 'PLTR',
      title: 'Earnings ahead: PLTR',
      message: 'PLTR reports on 2026-09-25 (estimated)',
      occurredAt: '2026-09-14T06:00:00Z',
      isRead: true,
      delivery: {
        eventId: 'event-2',
        disposition: 'Delivered',
        dispatchedAt: null,
        deliveredAt: '2026-09-14T06:30:00Z',
      },
      verdict: null,
      outcome: 'silent',
    },
  ],
  totalCount: 2,
  page: 1,
  pageSize: 20,
  totalPages: 1,
};

describeVisualBaseline({
  name: 'events',
  title: 'Events calendar',
  path: '/events',
  mocks: async page => {
    await mockGet(page, 'events/upcoming**', UPCOMING);
    await mockGet(page, 'events/fired**', FIRED);
  },
  ready: async page => {
    await expect(page.getByText('FOMC rate decision')).toBeVisible();
  },
});

describeVisualBaseline({
  name: 'events-fired',
  title: 'Events fired',
  path: '/events',
  mocks: async page => {
    await mockGet(page, 'events/upcoming**', UPCOMING);
    await mockGet(page, 'events/fired**', FIRED);
  },
  ready: async page => {
    await page.getByText('Fired', {exact: true}).click();
    await expect(page.getByText('already priced in')).toBeVisible();
  },
});
