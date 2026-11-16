import {defineRouteScans} from '@lifekit-hq/tokens/screen';
import {expect, test} from '@playwright/test';

import {defineByMode} from './screen-gates';

// The design judge (impeccable) and axe-core over every wired route, at 390 and 1280, light and
// dark (docs/ROUTE-SCAN.md in lifekit-common). A route in `report` mode prints what the judge and
// axe find; one in `enforce` fails on it. An accepted finding goes in `exceptions` with its reason,
// never one that names a library component: that is fixed once in lifekit-common. The two below
// are accepted until those issues land (Firstmate decision, #1341).
const exceptions = {
  impeccable: [
    {
      rule: 'nested-cards',
      routes: ['asset-dossier'],
      reason:
        'cmn-segmented in the Verdict card header reads as a card in a card; fixed in lifekit-common: https://github.com/lifekit-hq/lifekit-common/issues/397',
    },
    {
      rule: 'clipped-overflow-container',
      routes: ['asset-dossier'],
      // The judge reports this twice on the route; an exception covers one finding per scan by default.
      max: 2,
      reason:
        'shell overflow containers flagged for a positioned child on this route only; tracked in lifekit-common: https://github.com/lifekit-hq/lifekit-common/issues/398',
    },
  ],
};

defineByMode(test, (modeTest, routes) => {
  defineRouteScans(
    {test: modeTest, expect},
    {
      routes,
      exceptions: {
        impeccable: exceptions.impeccable.filter(entry =>
          entry.routes.every(name => routes.some(route => route.name === name))
        ),
      },
    }
  );
});
