import {defineRouteScans} from '@lifekit-hq/tokens/screen';
import {expect, test} from '@playwright/test';

import {defineByMode} from './screen-gates';

// The design judge (impeccable) and axe-core over every wired route, at 390 and 1280, light and
// dark (docs/ROUTE-SCAN.md in lifekit-common). A route in `report` mode prints what the judge and
// axe find; one in `enforce` fails on it. An accepted finding goes in `exceptions` with its reason,
// never one that names a library component: that is fixed once in lifekit-common. None are open.
const exceptions: {
  impeccable: {rule: string; routes: string[]; max?: number; reason: string}[];
} = {
  impeccable: [],
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
