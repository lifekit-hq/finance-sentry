import {SERIES} from '@lifekit-hq/charts-core';

/**
 * Chart-series steps a merchant avatar is keyed onto. Only the steps white initials stay legible
 * on: amber, lime and green are left out.
 */
export const MERCHANT_SERIES_STEPS: readonly number[] = [
  SERIES.accent,
  SERIES.violet,
  SERIES.pink,
  SERIES.blue,
  SERIES.red,
  SERIES.slate,
];
