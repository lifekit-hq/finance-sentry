import {CATEGORICAL_STEPS} from '@lifekit-hq/charts-core';

export class ChartColorUtils {
  /** CSS colour of series step `step` (1-based); follows the theme, so it is for styles, not canvas. */
  public static series(step: number): string {
    return `var(--color-chart-series-${step})`;
  }

  /**
   * CSS colour of the `index`-th categorical mark in a view. The categorical steps repeat only
   * every `CATEGORICAL_STEPS.length` marks, so neighbours in a list or grid never share one.
   */
  public static categorical(index: number): string {
    const steps = CATEGORICAL_STEPS.length;
    return ChartColorUtils.series(CATEGORICAL_STEPS[((index % steps) + steps) % steps]);
  }
}
