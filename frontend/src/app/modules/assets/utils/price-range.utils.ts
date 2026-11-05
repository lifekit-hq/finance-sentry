import {type Nullable} from '@lifekit-hq/ui';

import {
  SECONDS_PER_DAY,
  SECONDS_PER_HOUR,
  SECONDS_PER_MINUTE,
} from '../constants/price-chart/price-chart.constants';
import {
  type PriceBarDto,
  type RangeChange,
  type TimeSpan,
} from '../models/price-chart/price-chart.model';

const PERCENT = 100;

/** The move across a span of bars, for the visible-range readout and the measure tool. */
export class PriceRangeUtils {
  /**
   * First bar at or after `span.from` to last bar at or before `span.to`, close to close. Null when the span holds
   * fewer than two bars: there is no move to state across one bar.
   */
  public static change(
    bars: readonly PriceBarDto[],
    span: Nullable<TimeSpan>
  ): Nullable<RangeChange> {
    if (!span || bars.length === 0) {
      return null;
    }
    const lo = Math.min(span.from, span.to);
    const hi = Math.max(span.from, span.to);
    const first = bars.findIndex(b => b.time >= lo);
    if (first === -1) {
      return null;
    }
    let last = first;
    for (let i = bars.length - 1; i >= first; i--) {
      if (bars[i].time <= hi) {
        last = i;
        break;
      }
    }
    if (last <= first) {
      return null;
    }
    const from = bars[first];
    const to = bars[last];
    const change = to.close - from.close;
    return {
      fromTime: from.time,
      toTime: to.time,
      fromPrice: from.close,
      toPrice: to.close,
      change,
      changePct: from.close !== 0 ? (change / from.close) * PERCENT : null,
      seconds: to.time - from.time,
      bars: last - first + 1,
    };
  }

  /** "12 days", "1 day", or under a day "3h 25m". */
  public static spanLabel(seconds: number): string {
    if (seconds >= SECONDS_PER_DAY) {
      const days = Math.round(seconds / SECONDS_PER_DAY);
      return `${days} ${days === 1 ? 'day' : 'days'}`;
    }
    const hours = Math.floor(seconds / SECONDS_PER_HOUR);
    const minutes = Math.round((seconds % SECONDS_PER_HOUR) / SECONDS_PER_MINUTE);
    if (hours === 0) {
      return `${minutes}m`;
    }
    return minutes === 0 ? `${hours}h` : `${hours}h ${minutes}m`;
  }
}
