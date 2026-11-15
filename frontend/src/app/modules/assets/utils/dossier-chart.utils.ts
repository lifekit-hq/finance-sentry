import {periodStart, type PricePeriod, type PriceSeries} from '@lifekit-hq/charts-core';
import {type Nullable} from '@lifekit-hq/ui';

import {MS_PER_DAY} from '../../../shared/constants/date/date.constants';
import {DateUtils} from '../../../shared/utils/date.utils';
import {MoneyUtils} from '../../../shared/utils/money.utils';
import {
  AVERAGE_COST_LABEL,
  BENCHMARK_START_TOLERANCE_DAYS,
} from '../constants/dossier/dossier-chart.constants';
import {type AssetChartDto, type AssetChartTradeDto} from '../models/dossier/dossier.model';
import {type DossierChartView} from '../models/dossier/dossier-view.model';

export class DossierChartUtils {
  /**
   * The whole history as the price block's series: the asset's closes, then the S&P 500 on the days it
   * has a close (the block holds its last price over a day it had none). The block cuts them to the
   * period and reads percent while the index overlays. The index is left out when it starts more than
   * a few days after the asset's first close in `period`, as it would otherwise sit flat at its first
   * value over the days before it existed. Null when there is no history to draw.
   */
  public static view(
    chart: Nullable<AssetChartDto>,
    period: PricePeriod
  ): Nullable<DossierChartView> {
    if (!chart || chart.points.length === 0) {
      return null;
    }
    const series: PriceSeries[] = [
      {
        label: chart.symbol,
        points: chart.points.map(p => ({time: Date.parse(p.date), value: p.close})),
      },
    ];

    const benchmarkPoints = chart.points.flatMap(p =>
      p.benchmark === null ? [] : [{time: Date.parse(p.date), value: p.benchmark}]
    );
    const assetTimes = series[0].points.map(p => p.time);
    const periodFloor = periodStart(period, assetTimes[assetTimes.length - 1]);
    const periodOpen = assetTimes.find(time => time >= periodFloor);
    const hasBenchmark =
      benchmarkPoints.length > 0 &&
      periodOpen !== undefined &&
      benchmarkPoints[0].time - periodOpen <= BENCHMARK_START_TOLERANCE_DAYS * MS_PER_DAY;
    if (hasBenchmark) {
      series.push({label: chart.benchmarkLabel, points: benchmarkPoints});
    }

    const averageCost = chart.averageCost;
    const averageCostLabel =
      averageCost !== null && averageCost > 0
        ? `${AVERAGE_COST_LABEL} ${MoneyUtils.format(averageCost, chart.currency)}`
        : null;

    return {
      series,
      caption: DossierChartUtils.caption(chart.symbol, hasBenchmark && chart.benchmarkLabel),
      averageCostLabel,
      trades: chart.trades.map(trade => ({
        side: trade.side,
        title: DossierChartUtils.tradeTitle(trade),
      })),
    };
  }

  /** "Bought 5 at $110.00 · Tue 14 Oct 2025". */
  public static tradeTitle(trade: AssetChartTradeDto): string {
    const verb = trade.side === 'buy' ? 'Bought' : 'Sold';
    const price = MoneyUtils.format(trade.price, trade.currency);
    return `${verb} ${trade.quantity} at ${price} · ${DateUtils.dayLabel(trade.date, true)}`;
  }

  /** What each line is and what the dotted one marks, for the block's caption. */
  public static caption(symbol: string, benchmarkLabel: string | false): string {
    return benchmarkLabel
      ? `${symbol} (first line) against ${benchmarkLabel} (second line), each as its change since the period began; the dotted line marks the start of the period.`
      : `${symbol} price (solid line); the dotted line marks the price at the start of the period.`;
  }
}
