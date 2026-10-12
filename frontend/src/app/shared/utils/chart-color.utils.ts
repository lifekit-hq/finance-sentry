import {CATEGORICAL_STEPS, cssVar, seriesColor} from '@lifekit-hq/charts-core';

import {type ChartColour, type FixedColourName} from '../models/chart-colour/chart-colour.model';

export class ChartColorUtils {
  /** CSS colour of series step `step` (1-based); follows the theme, so it is for styles, not canvas. */
  public static series(step: number): string {
    return `var(--color-chart-series-${step})`;
  }

  /** CSS colour of a fixed-meaning token; constant across seeds, so it is for styles, not canvas. */
  public static fixed(name: FixedColourName): string {
    return `var(--color-${name})`;
  }

  /** CSS colour of a chart colour, for legends and swatches. */
  public static css(colour: ChartColour): string {
    return 'fixed' in colour
      ? ChartColorUtils.fixed(colour.fixed)
      : ChartColorUtils.series(colour.step);
  }

  /** Resolved colour of a chart colour for a canvas, which cannot read a CSS var. */
  public static canvas(colour: ChartColour): string {
    return 'fixed' in colour
      ? cssVar(`--color-${colour.fixed}`, 'currentColor')
      : seriesColor(colour.step);
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
