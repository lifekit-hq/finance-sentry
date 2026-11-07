import {checkScreen, formatScreen, SCREEN_VIEWPORTS} from '@lifekit-hq/tokens/screen';
import {test} from '@playwright/test';

import {FindingError, reportingBody, type ScreenTestBody} from './report-only';
import {wiredRoutes} from './routes';
import {toGateRoute} from './screen-gates';

// The screen-composition check (rules C0 to C10) on every wired route, at both sizes and both
// schemes. A route in `report` mode prints and annotates what the check finds; one in `enforce`
// fails on it (docs/SCREEN-CHECK.md in lifekit-common). The expected page kind of each route is
// `kind` in routes.ts; `checkScreen` reads the kind the page declares, so a mismatch is reported.
for (const [size, viewport] of Object.entries(SCREEN_VIEWPORTS)) {
  test.describe(`screen check ${size}`, () => {
    test.use({...viewport, timezoneId: 'UTC', locale: 'en-US'});

    for (const scheme of ['light', 'dark'] as const) {
      for (const route of wiredRoutes()) {
        const gate = toGateRoute(route);
        const run: ScreenTestBody = async ({page}) => {
          await page.emulateMedia({colorScheme: scheme, reducedMotion: 'reduce'});
          await gate.prepare(page);
          await page.goto(gate.url);
          await gate.ready(page);
          const result = await checkScreen(page, {kind: route.kind});
          const kindNote =
            result.kind === route.kind
              ? ''
              : `\nexpected page kind ${route.kind ?? 'none'}, the page declares ${result.kind ?? 'none'}`;
          if (result.findings.length > 0 || kindNote) {
            throw new FindingError(formatScreen(result) + kindNote);
          }
        };
        test(`${route.name} ${scheme}`, route.mode === 'enforce' ? run : reportingBody(run));
      }
    }
  });
}
