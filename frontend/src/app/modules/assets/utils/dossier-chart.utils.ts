import {type AreaSeries, type Nullable} from '@lifekit-hq/ui';

import {ChartColorUtils} from '../../../shared/utils/chart-color.utils';
import {DateUtils} from '../../../shared/utils/date.utils';
import {MoneyUtils} from '../../../shared/utils/money.utils';
import {
  AVERAGE_COST_LABEL,
  AVERAGE_COST_LINE_COLOUR,
  BENCHMARK_LINE_COLOUR,
  PRICE_LINE_COLOUR,
  PRICE_LINE_LABEL,
} from '../constants/dossier/dossier-chart.constants';
import {type AssetChartDto, type AssetChartTradeDto} from '../models/dossier/dossier.model';
import {type ChartTradeTick, type DossierChartView} from '../models/dossier/dossier-view.model';

const FULL_WIDTH_PCT = 100;
const CENTRE_PCT = 50;

export class DossierChartUtils {
  /**
   * The chart as index-aligned lines: the price, the S&P 500 rebased onto its first close, and the
   * user's average cost held flat. Null when there is no history to draw.
   */
  public static view(chart: Nullable<AssetChartDto>): Nullable<DossierChartView> {
    if (!chart || chart.points.length === 0) {
      return null;
    }
    const labels = chart.points.map(p => DateUtils.dayLabel(p.date, true));
    const series: AreaSeries[] = [
      {
        label: PRICE_LINE_LABEL,
        color: ChartColorUtils.canvas(PRICE_LINE_COLOUR),
        points: chart.points.map((p, i) => ({label: labels[i], value: p.close})),
      },
    ];

    const benchmark = DossierChartUtils.filledBenchmark(chart);
    if (benchmark) {
      series.push({
        label: chart.benchmarkLabel,
        color: ChartColorUtils.canvas(BENCHMARK_LINE_COLOUR),
        points: benchmark.map((value, i) => ({label: labels[i], value})),
      });
    }

    const averageCost = chart.averageCost;
    let averageCostLabel: Nullable<string> = null;
    if (averageCost !== null && averageCost > 0) {
      series.push({
        label: AVERAGE_COST_LABEL,
        color: ChartColorUtils.canvas(AVERAGE_COST_LINE_COLOUR),
        points: labels.map(label => ({label, value: averageCost})),
      });
      averageCostLabel = `${AVERAGE_COST_LABEL} ${MoneyUtils.format(averageCost, chart.currency)}`;
    }

    return {series, averageCostLabel, ticks: DossierChartUtils.ticks(chart)};
  }

  /**
   * Each fill placed at the first close on or after its day, as a share of the axis: the chart
   * spaces its closes evenly, so a tick sits under the close it moved. Fills outside the window
   * are already dropped by the API; a fill after the last close pins to the end.
   */
  public static ticks(chart: AssetChartDto): ChartTradeTick[] {
    const points = chart.points;
    if (points.length === 0) {
      return [];
    }
    const lastIndex = points.length - 1;
    return chart.trades.map(trade => {
      const found = points.findIndex(p => p.date >= trade.date);
      const index = found === -1 ? lastIndex : found;
      const leftPct = lastIndex === 0 ? CENTRE_PCT : (index / lastIndex) * FULL_WIDTH_PCT;
      return {leftPct, side: trade.side, title: DossierChartUtils.tradeTitle(trade)};
    });
  }

  /** "Bought 5 at $110.00 · Tue 14 Oct 2025". */
  public static tradeTitle(trade: AssetChartTradeDto): string {
    const verb = trade.side === 'buy' ? 'Bought' : 'Sold';
    const price = MoneyUtils.format(trade.price, trade.currency);
    return `${verb} ${trade.quantity} at ${price} · ${DateUtils.dayLabel(trade.date, true)}`;
  }

  /**
   * The benchmark as a complete line: a day the index had no close carries the previous close
   * (leading gaps take the first one). Null when the API reports it does not cover the window
   * (`benchmarkChangePct` null), so the line and the percentage never disagree.
   */
  private static filledBenchmark(chart: AssetChartDto): Nullable<number[]> {
    if (chart.benchmarkChangePct === null) {
      return null;
    }
    const first = chart.points.find(p => p.benchmark !== null)?.benchmark ?? null;
    if (first === null) {
      return null;
    }
    let last = first;
    return chart.points.map(p => {
      if (p.benchmark !== null) {
        last = p.benchmark;
      }
      return last;
    });
  }
}
