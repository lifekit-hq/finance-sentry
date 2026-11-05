import {formatDate} from '@angular/common';
import {type Nullable} from '@lifekit-hq/ui';

import {APP_LOCALE} from '../../../shared/constants/date/date.constants';
import {MoneyUtils} from '../../../shared/utils/money.utils';
import {PercentUtils} from '../../../shared/utils/percent.utils';
import {
  MOVING_AVERAGES,
  PRICE_INTERVALS,
  PRICE_PERIODS,
} from '../constants/price-chart/price-chart.constants';
import {
  type AssetPriceBarsDto,
  type MovingAverageGap,
  type PriceBarDto,
  type PriceIntervalId,
  type RangeChange,
} from '../models/price-chart/price-chart.model';
import {PriceRangeUtils} from './price-range.utils';

const PERCENT_DIGITS = 1;
const SUB_DOLLAR_PRECISION = 4;
const DEFAULT_PRECISION = 2;
const SUB_DOLLAR = 1;
const COMPACT_MIN = 1000;
const UTC = 'UTC';
const DAY_FORMAT = 'd MMM y';
const INTRADAY_FORMAT = 'd MMM y, HH:mm';
const MS_PER_SECOND = 1000;
const INTRADAY: readonly PriceIntervalId[] = ['5m', '15m', '1h'];

/** The words around the chart: the screen-reader summary, the readouts and the axis precision. */
export class PriceChartViewUtils {
  /** What a screen reader gets in place of the canvas. */
  public static summary(bars: Nullable<AssetPriceBarsDto>, visible: Nullable<RangeChange>): string {
    if (!bars || bars.bars.length === 0) {
      return 'Price chart: no price history.';
    }
    const last = bars.bars[bars.bars.length - 1];
    const parts = [
      `${bars.symbol} price chart, ${PriceChartViewUtils.periodLabel(bars)} in ${PriceChartViewUtils.intervalLabel(bars)} candles.`,
      `Last close ${MoneyUtils.format(last.close, bars.currency)}.`,
    ];
    if (visible) {
      parts.push(`In view: ${PriceChartViewUtils.move(visible, bars.currency)}.`);
    }
    return parts.join(' ');
  }

  /** "+$12.40 (+7.0%) over 120 days". */
  public static move(change: RangeChange, currency: Nullable<string>): string {
    const amount = MoneyUtils.format(change.change, currency, {signed: true});
    const pct =
      change.changePct === null
        ? ''
        : ` (${PercentUtils.signed(change.changePct, PERCENT_DIGITS)})`;
    return `${amount}${pct} over ${PriceRangeUtils.spanLabel(change.seconds)}`;
  }

  public static periodLabel(bars: AssetPriceBarsDto): string {
    return PRICE_PERIODS.find(p => p.value === bars.period)?.label ?? bars.period;
  }

  public static intervalLabel(bars: AssetPriceBarsDto): string {
    return PRICE_INTERVALS.find(i => i.value === bars.interval)?.label ?? bars.interval;
  }

  /** Decimal places for the price axis: a sub-dollar price needs more than cents to move. */
  public static precision(bars: readonly {low: number}[]): number {
    const lowest = bars.reduce((min, b) => Math.min(min, b.low), Infinity);
    return lowest < SUB_DOLLAR ? SUB_DOLLAR_PRECISION : DEFAULT_PRECISION;
  }

  /** Volume as "1.2M" / "34.5K" / "812". */
  public static volume(volume: number): string {
    if (volume < COMPACT_MIN) {
      return String(volume);
    }
    return new Intl.NumberFormat('en-US', {notation: 'compact', maximumFractionDigits: 1}).format(
      volume
    );
  }

  /** A move as text plus whether it is a gain (zero reads as one: only a loss is red). */
  public static readout(
    change: Nullable<RangeChange>,
    currency: Nullable<string>
  ): Nullable<MoveReadout> {
    return change
      ? {text: PriceChartViewUtils.move(change, currency), isGain: change.change >= 0}
      : null;
  }

  public static isIntraday(interval: PriceIntervalId): boolean {
    return INTRADAY.includes(interval);
  }

  /** A bar's start on the exchange's own clock: "12 Oct 2026" for a day or longer, with the time under a day. */
  public static timeLabel(time: number, interval: PriceIntervalId): string {
    return formatDate(
      time * MS_PER_SECOND,
      PriceChartViewUtils.isIntraday(interval) ? INTRADAY_FORMAT : DAY_FORMAT,
      APP_LOCALE,
      UTC
    );
  }

  /** The crosshair legend: the bar's start, OHLC and volume, formatted. */
  public static legend(bar: PriceBarDto, bars: AssetPriceBarsDto): LegendView {
    const price = (value: number): string => MoneyUtils.format(value, bars.currency);
    return {
      time: PriceChartViewUtils.timeLabel(bar.time, bars.interval),
      open: price(bar.open),
      high: price(bar.high),
      low: price(bar.low),
      close: price(bar.close),
      volume: bar.volume > 0 ? PriceChartViewUtils.volume(bar.volume) : null,
    };
  }

  /** "52-week range $120.00 - $210.50"; a younger series says since when instead. Null with no range. */
  public static weekRangeText(bars: Nullable<AssetPriceBarsDto>): Nullable<string> {
    const week = bars?.weekRange;
    if (!bars || !week) {
      return null;
    }
    const range = `${MoneyUtils.format(week.low, bars.currency)} - ${MoneyUtils.format(week.high, bars.currency)}`;
    return week.coversYear
      ? `52-week range ${range}`
      : `Range since ${formatDate(`${week.from}T00:00:00Z`, DAY_FORMAT, APP_LOCALE, UTC)} ${range} (less than 52 weeks of history)`;
  }

  /** Said when the asset's history starts after the period does, so a short chart is not mistaken for a short period. */
  public static truncatedNote(bars: Nullable<AssetPriceBarsDto>): Nullable<string> {
    if (!bars || !bars.truncated || bars.bars.length === 0) {
      return null;
    }
    return `Price history starts ${PriceChartViewUtils.timeLabel(bars.windowStart, bars.interval)}, so this period is shorter than asked.`;
  }

  /** Why a switched-on average draws nothing: it needs more bars than the source has. */
  public static gapNote(gaps: readonly MovingAverageGap[]): Nullable<string> {
    if (gaps.length === 0) {
      return null;
    }
    const parts = gaps.map(gap => {
      const label = MOVING_AVERAGES.find(m => m.id === gap.id)?.label ?? gap.id;
      return `${label} needs ${gap.needed} bars (${gap.available} available)`;
    });
    return `Not drawn: ${parts.join('; ')}.`;
  }
}

export interface MoveReadout {
  text: string;
  isGain: boolean;
}

export interface LegendView {
  time: string;
  open: string;
  high: string;
  low: string;
  close: string;
  volume: Nullable<string>;
}
