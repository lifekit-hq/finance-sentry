import type {Page, test as playwrightTest} from '@playwright/test';

import {prepareScreen, settleCharts, versionMasks} from '../support/visual-baseline';
import {asSetup, reportOnly} from './report-only';
import {type RouteMode, type WiredRoute, wiredRoutes} from './routes';

/** What the shared fixtures take of a route: how to prepare it, open it and know it has rendered. */
export interface GateRoute {
  name: string;
  url: string;
  prepare: (page: Page) => Promise<void>;
  ready: (page: Page) => Promise<void>;
  mask: (page: Page) => ReturnType<typeof versionMasks>;
}

export function toGateRoute(route: WiredRoute): GateRoute {
  return {
    name: route.name,
    url: route.path,
    prepare: page => asSetup('prepare', () => prepareScreen(page, route.fixture)),
    ready: page =>
      asSetup('ready', async () => {
        await route.fixture.ready(page);
        await settleCharts(page);
      }),
    // The release version prints in the sidebar and changes on every release.
    mask: versionMasks,
  };
}

/**
 * Splits the wired routes by mode and hands each group to `define` with the `test` that fits it:
 * the real one for `enforce` (a finding fails), the reporting one for `report` (a finding is printed
 * and annotated, the test passes). `pending` routes are not opened.
 */
export function defineByMode(
  test: typeof playwrightTest,
  define: (test: typeof playwrightTest, routes: GateRoute[]) => void
): void {
  const modes: Record<Exclude<RouteMode, 'pending'>, typeof playwrightTest> = {
    enforce: test,
    report: reportOnly(test) as typeof playwrightTest,
  };
  for (const [mode, modeTest] of Object.entries(modes)) {
    const routes = wiredRoutes()
      .filter(route => route.mode === mode)
      .map(toGateRoute);
    if (routes.length > 0) {
      define(modeTest, routes);
    }
  }
}
