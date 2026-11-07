import {expect, test} from '@playwright/test';

import {FindingError, REPORT_ONLY_ANNOTATION, reportOnly} from './report-only';
import {toGateRoute} from './screen-gates';

const reportTest = reportOnly(test) as typeof test;

const BROKEN_READY_ROUTE = toGateRoute({
  name: 'broken-ready',
  path: '/',
  kind: null,
  note: 'a route whose ready element never renders',
  mode: 'report',
  fixture: {
    mocks: () => Promise.resolve(),
    ready: () => Promise.reject(new Error('the ready element never appeared')),
  },
});

// A route in `report` mode swallows what the gates find, never a failure to open the route: a
// renamed test id in a route's `ready` must turn the test red, not green with nothing checked.
test.describe('report mode: a route that cannot be opened fails', () => {
  reportTest('its ready assertion throws', async ({page}) => {
    test.fail();
    await BROKEN_READY_ROUTE.prepare(page);
    await BROKEN_READY_ROUTE.ready(page);
  });

  reportTest('a navigation or settle error is not a finding', () => {
    test.fail();
    return Promise.reject(new Error('page.goto: net::ERR_CONNECTION_REFUSED'));
  });
});

test.describe('report mode: a gate finding is annotated, not failed', () => {
  test.afterEach(({}, info) => {
    expect(info.annotations.map(annotation => annotation.type)).toContain(REPORT_ONLY_ANNOTATION);
  });

  reportTest('a failed matcher', () => {
    expect(2, 'the screen is two').toBe(1);
    return Promise.resolve();
  });

  reportTest('a finding error', () => Promise.reject(new FindingError('C3: no page heading')));
});
