import {defineScreenBaselines} from '@lifekit-hq/tokens/screen';
import {expect, test} from '@playwright/test';

import {defineByMode} from './screen-gates';

// Pixel baselines for every routed screen: 390 and 1280 wide, light and dark, against the Linux
// images committed beside this file (`baselines.spec.ts-snapshots`). Rendered by the "Update screen
// baselines" workflow on the CI runner (docs/SCREEN-BASELINES.md in lifekit-common).
// The existing per-screen specs (`*-visual.spec.ts`) keep their own baselines until a route moves here.
defineByMode(test, (modeTest, routes) => {
  defineScreenBaselines({test: modeTest, expect}, {routes});
});
