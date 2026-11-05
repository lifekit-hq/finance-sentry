import {ColourAlphaUtils} from '../../../shared/utils/colour-alpha.utils';
import {VOLUME_ALPHA} from '../constants/price-chart/price-chart.constants';
import {type LinePoint, type PriceBarDto} from '../models/price-chart/price-chart.model';

export interface CandlePoint {
  time: number;
  open: number;
  high: number;
  low: number;
  close: number;
}

export interface VolumePoint {
  time: number;
  value: number;
  color: string;
}

/** Bars reshaped for the charting library; `time` stays the exchange-clock epoch seconds. */
export class PriceChartDataUtils {
  public static candles(bars: readonly PriceBarDto[]): CandlePoint[] {
    return bars.map(({time, open, high, low, close}) => ({time, open, high, low, close}));
  }

  public static closes(bars: readonly PriceBarDto[]): LinePoint[] {
    return bars.map(b => ({time: b.time, value: b.close}));
  }

  /** Volume tinted gain where the bar closed at or above its open, loss where below. */
  public static volumes(bars: readonly PriceBarDto[], gain: string, loss: string): VolumePoint[] {
    const up = ColourAlphaUtils.withAlpha(gain, VOLUME_ALPHA);
    const down = ColourAlphaUtils.withAlpha(loss, VOLUME_ALPHA);
    return bars.map(b => ({time: b.time, value: b.volume, color: b.close >= b.open ? up : down}));
  }

  /** Whether any bar traded: an index or FX pair reports no volume, and an empty pane is not drawn. */
  public static hasVolume(bars: readonly PriceBarDto[]): boolean {
    return bars.some(b => b.volume > 0);
  }
}
