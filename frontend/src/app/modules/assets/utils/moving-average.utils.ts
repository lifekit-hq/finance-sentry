import {MOVING_AVERAGES} from '../constants/price-chart/price-chart.constants';
import {
  type LinePoint,
  type MovingAverageGap,
  type MovingAverageId,
  type MovingAverageLine,
  type MovingAverageSpec,
  type PriceBarDto,
} from '../models/price-chart/price-chart.model';

const EMA_SMOOTHING_NUMERATOR = 2;

/** Simple and exponential moving averages over a bar series' closes. */
export class MovingAverageUtils {
  /** Rolling mean of the last `length` values; null until `length` values exist. */
  public static sma(values: readonly number[], length: number): (number | null)[] {
    const out: (number | null)[] = [];
    let sum = 0;
    for (let i = 0; i < values.length; i++) {
      sum += values[i];
      if (i >= length) {
        sum -= values[i - length];
      }
      out.push(i >= length - 1 ? sum / length : null);
    }
    return out;
  }

  /**
   * Exponential average with weight 2 / (length + 1), seeded with the simple mean of the first `length`
   * values (the usual charting convention); null before that seed.
   */
  public static ema(values: readonly number[], length: number): (number | null)[] {
    const out: (number | null)[] = [];
    const weight = EMA_SMOOTHING_NUMERATOR / (length + 1);
    let previous = 0;
    let seed = 0;
    for (let i = 0; i < values.length; i++) {
      if (i < length - 1) {
        seed += values[i];
        out.push(null);
      } else if (i === length - 1) {
        seed += values[i];
        previous = seed / length;
        out.push(previous);
      } else {
        previous = values[i] * weight + previous * (1 - weight);
        out.push(previous);
      }
    }
    return out;
  }

  /** The line for one average over `bars`: only the bars where it is defined. */
  public static line(bars: readonly PriceBarDto[], spec: MovingAverageSpec): MovingAverageLine {
    const closes = bars.map(b => b.close);
    const values =
      spec.kind === 'sma'
        ? MovingAverageUtils.sma(closes, spec.length)
        : MovingAverageUtils.ema(closes, spec.length);
    const points: LinePoint[] = [];
    values.forEach((value, i) => {
      if (value !== null) {
        points.push({time: bars[i].time, value});
      }
    });
    return {spec, points};
  }

  /** The lines for the averages switched on, in the catalogue's order. */
  public static lines(
    bars: readonly PriceBarDto[],
    selected: readonly MovingAverageId[]
  ): MovingAverageLine[] {
    return MOVING_AVERAGES.filter(spec => selected.includes(spec.id)).map(spec =>
      MovingAverageUtils.line(bars, spec)
    );
  }

  /**
   * The averages that cannot be drawn at all because fewer bars are loaded than they need: the chart
   * says so instead of drawing nothing.
   */
  public static gaps(
    bars: readonly PriceBarDto[],
    selected: readonly MovingAverageId[]
  ): MovingAverageGap[] {
    return MOVING_AVERAGES.filter(
      spec => selected.includes(spec.id) && bars.length < spec.length
    ).map(spec => ({id: spec.id, needed: spec.length, available: bars.length}));
  }

  /** Whether an average the bars are too few for is switched on - which ones cannot be drawn. */
  public static unavailable(bars: readonly PriceBarDto[]): MovingAverageId[] {
    return MOVING_AVERAGES.filter(spec => bars.length < spec.length).map(spec => spec.id);
  }
}
