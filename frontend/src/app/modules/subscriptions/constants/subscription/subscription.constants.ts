import {CATEGORICAL_STEPS, NEUTRAL_STEP} from '@lifekit-hq/charts-core';

/**
 * Chart-series steps a merchant avatar is keyed onto: the positions in the seed's categorical ramp
 * (the first, third, fifth and eighth) whose light colours hold white initials at 4.5:1 or more,
 * then the neutral step.
 */
export const MERCHANT_SERIES_STEPS: readonly number[] = [
  CATEGORICAL_STEPS[0],
  CATEGORICAL_STEPS[2],
  CATEGORICAL_STEPS[4],
  CATEGORICAL_STEPS[7],
  NEUTRAL_STEP,
];
