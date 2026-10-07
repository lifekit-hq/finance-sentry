import {SERIES} from '@lifekit-hq/charts-core';

/**
 * Chart-series steps for categorical marks, in the order the library donut cycles them. Slate is
 * left out: it is the colour of a grouped "other" tail, never of a named category.
 */
export const CATEGORICAL_SERIES_STEPS: readonly number[] = [
  SERIES.accent,
  SERIES.amber,
  SERIES.violet,
  SERIES.lime,
  SERIES.pink,
  SERIES.blue,
  SERIES.green,
  SERIES.red,
];

/** The series step of a grouped tail ("Other categories") and of a mark with nothing to key on. */
export const OTHER_SERIES_STEP: number = SERIES.slate;
