import {expect, type Page, test} from '@playwright/test';

import {FindingError, REPORT_ONLY_ANNOTATION, reportOnly} from './report-only';
import type {WiredRoute} from './routes';
import {type GateRoute, toGateRoute} from './screen-gates';

const reportTest = reportOnly(test) as typeof test;

const NEVER_RENDERED_TIMEOUT_MS = 100;

const neverRenders = (page: Page): Promise<void> =>
  expect(page.getByTestId('never-rendered')).toBeVisible({timeout: NEVER_RENDERED_TIMEOUT_MS});

const brokenRoute = (name: string, fixture: WiredRoute['fixture']): GateRoute =>
  toGateRoute({
    name,
    path: '/',
    kind: null,
    note: 'a route that cannot be opened',
    mode: 'report',
    fixture,
  });

const BROKEN_READY_ROUTE = brokenRoute('broken-ready', {
  mocks: () => Promise.resolve(),
  ready: neverRenders,
});

const BROKEN_MOCKS_ROUTE = brokenRoute('broken-mocks', {
  mocks: neverRenders,
  ready: () => Promise.resolve(),
});

// A route in `report` mode swallows what the gates find, never a failure to open the route: a
// renamed test id in a route's `ready` (an expect timeout, which carries a matcher result like any
// finding) must turn the test red, not green with nothing checked.
test.describe('report mode: a route that cannot be opened fails', () => {
  reportTest('its ready assertion times out', async ({page}) => {
    test.fail();
    await BROKEN_READY_ROUTE.ready(page);
  });

  reportTest('its mocks assertion times out', async ({page}) => {
    test.fail();
    await BROKEN_MOCKS_ROUTE.prepare(page);
  });

  reportTest('a navigation or settle error is not a finding', () => {
    test.fail();
    return Promise.reject(new Error('page.goto: net::ERR_CONNECTION_REFUSED'));
  });
});

test.describe('report mode: a gate finding is annotated, not failed', () => {
  // eslint-disable-next-line no-empty-pattern -- Playwright needs a destructured first parameter
  test.afterEach(({}, info) => {
    expect(info.annotations.map(annotation => annotation.type)).toContain(REPORT_ONLY_ANNOTATION);
  });

  reportTest('a failed matcher', () => {
    expect(2, 'the screen is two').toBe(1);
    return Promise.resolve();
  });

  reportTest('a finding error', () => Promise.reject(new FindingError('C3: no page heading')));
});
