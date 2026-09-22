import {expect, type Page, test} from '@playwright/test';

const API = '**/api/v1';

const AUTH_RESPONSE = {
  user: {id: 'test-user-id', email: 'test@gmail.com'},
  expiresAt: '2027-01-01T00:00:00Z',
};

const DATE_PART_PAD = 2;

function isoDate(daysFromToday: number): string {
  const d = new Date();
  d.setDate(d.getDate() + daysFromToday);
  const month = String(d.getMonth() + 1).padStart(DATE_PART_PAD, '0');
  const day = String(d.getDate()).padStart(DATE_PART_PAD, '0');
  return `${d.getFullYear()}-${month}-${day}`;
}

const UPCOMING = {
  items: [
    {
      kind: 'earnings',
      date: isoDate(0),
      time: null,
      subject: 'MU',
      title: 'Earnings: MU',
      detail: null,
      isEstimate: true,
      source: 'yahoo',
      referenceId: null,
    },
    {
      kind: 'macro',
      date: isoDate(3),
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
      date: isoDate(9),
      time: null,
      subject: 'MU',
      title: 'Thesis catalyst: MU',
      detail: 'HBM4 ramp update',
      isEstimate: false,
      source: 'thesis',
      referenceId: 'thesis-1',
    },
  ],
  from: isoDate(0),
  to: isoDate(30),
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
      occurredAt: '2026-09-22T09:35:00Z',
      isRead: false,
      delivery: {
        eventId: 'event-1',
        disposition: 'Delivered',
        dispatchedAt: '2026-09-22T09:36:00Z',
        deliveredAt: '2026-09-22T09:40:00Z',
      },
      verdict: {
        text: 'Two of the three pieces restate the same guidance cut; already priced in.',
        notified: false,
        recordedAt: '2026-09-22T09:41:00Z',
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
      occurredAt: '2026-09-22T06:00:00Z',
      isRead: true,
      delivery: {
        eventId: 'event-2',
        disposition: 'Delivered',
        dispatchedAt: null,
        deliveredAt: '2026-09-22T06:30:00Z',
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

async function mockApis(page: Page): Promise<void> {
  const json = (body: unknown) => ({contentType: 'application/json', body: JSON.stringify(body)});
  await page.route(`${API}/auth/me`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/auth/refresh`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/alerts/unread-count`, route => route.fulfill(json({count: 0})));
  await page.route(`${API}/alerts?**`, route =>
    route.fulfill(
      json({items: [], totalCount: 0, unreadCount: 0, page: 1, pageSize: 20, totalPages: 0})
    )
  );
  await page.route(`${API}/events/upcoming**`, route => route.fulfill(json(UPCOMING)));
  await page.route(`${API}/events/fired**`, route => route.fulfill(json(FIRED)));
}

test.describe('Events', () => {
  test.beforeEach(async ({page}) => {
    await mockApis(page);
  });

  test('calendar view groups upcoming events by day and flags a missing source', async ({page}) => {
    await page.goto('/events');

    await expect(page.getByRole('heading', {name: 'Events', level: 1})).toBeVisible();
    await expect(page.getByText('Today')).toBeVisible();
    await expect(page.getByText('Earnings: MU')).toBeVisible();
    await expect(page.getByText('FOMC rate decision')).toBeVisible();
    await expect(page.getByText('HBM4 ramp update')).toBeVisible();
    await expect(page.getByTestId('sources-unavailable')).toContainText('filing due dates');
  });

  test('changing the horizon re-queries the calendar with the new window', async ({page}) => {
    const requests: string[] = [];
    page.on('request', req => {
      if (req.url().includes('/events/upcoming')) {
        requests.push(req.url());
      }
    });
    await page.goto('/events');
    await expect(page.getByText('Earnings: MU')).toBeVisible();

    await page.getByText('90 days').click();

    await expect.poll(() => requests.some(u => u.includes(`to=${isoDate(90)}`))).toBe(true);
  });

  test('fired view shows each event with its outcome and the recorded verdict', async ({page}) => {
    await page.goto('/events');
    await page.getByText('Fired', {exact: true}).click();

    await expect(page.getByText('News cluster: MU')).toBeVisible();
    await expect(page.getByText('Judged immaterial')).toBeVisible();
    await expect(page.getByText('already priced in')).toBeVisible();
    await expect(page.getByText('Silent', {exact: true})).toBeVisible();
    await expect(page.getByText('Earnings ahead: PLTR')).toBeVisible();
  });
});
