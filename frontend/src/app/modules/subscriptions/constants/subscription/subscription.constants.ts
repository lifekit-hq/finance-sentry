import {CATEGORICAL_STEPS, NEUTRAL_STEP} from '@lifekit-hq/charts-core';

/**
 * Chart-series steps a merchant avatar is keyed onto: the positions in the seed's categorical ramp
 * (the first, third, fifth, sixth and seventh) on which white initials stay legible, then the
 * neutral step.
 */
export const MERCHANT_SERIES_STEPS: readonly number[] = [
  CATEGORICAL_STEPS[0],
  CATEGORICAL_STEPS[2],
  CATEGORICAL_STEPS[4],
  CATEGORICAL_STEPS[5],
  CATEGORICAL_STEPS[6],
  NEUTRAL_STEP,
];
